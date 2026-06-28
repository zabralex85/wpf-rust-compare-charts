use std::time::{Instant, SystemTime, UNIX_EPOCH};

use futures_util::{SinkExt, StreamExt};
use rusqlite::Connection;
use tokio::net::TcpListener;
use tokio_tungstenite::tungstenite::Message;

use crate::db::{load_channels, load_enum_values, load_samples};
use crate::frame::{FrameMessage, MetaMessage, MetricsMessage};
use crate::metrics::MetricsSampler;
use crate::replay::Pacer;

#[derive(Clone)]
pub struct ServerConfig {
    pub db_path: String,
    pub port: u16,
    pub speed: f64,
}

fn now_unix_ms() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap()
        .as_millis() as i64
}

pub async fn serve(config: ServerConfig) -> std::io::Result<()> {
    let listener = TcpListener::bind(("127.0.0.1", config.port)).await?;
    serve_on(listener, config).await;
    Ok(())
}

pub async fn serve_on(listener: TcpListener, config: ServerConfig) {
    loop {
        let (stream, _) = match listener.accept().await {
            Ok(x) => x,
            Err(_) => continue,
        };
        let cfg = config.clone();
        tokio::spawn(async move {
            let _ = handle_client(stream, cfg).await;
        });
    }
}

async fn handle_client(
    stream: tokio::net::TcpStream,
    cfg: ServerConfig,
) -> anyhow::Result<()> {
    let ws = tokio_tungstenite::accept_async(stream).await?;

    // Load DB on a blocking thread (rusqlite is sync).
    let db_path = cfg.db_path.clone();
    let (meta_json, samples) = tokio::task::spawn_blocking(move || {
        let conn = Connection::open(&db_path)?;
        let channels = load_channels(&conn)?;
        let enums = load_enum_values(&conn)?;
        let rate: i64 =
            conn.query_row("SELECT rate_hz FROM ride_meta", [], |r| r.get(0))?;
        let duration_s: i64 =
            conn.query_row("SELECT duration_s FROM ride_meta", [], |r| r.get(0))?;
        let samples = load_samples(&conn, &channels)?;
        let meta = MetaMessage::new(channels, enums, rate, duration_s);
        Ok::<_, rusqlite::Error>((serde_json::to_string(&meta).unwrap(), samples))
    })
    .await??;

    // Split the WebSocket into independent write and read halves so the reader
    // task can own `read` while the main loop holds `write`.
    let (mut write, mut read) = ws.split();

    // Send meta first; keep `meta_json` for re-sending on seek.
    write.send(Message::Text(meta_json.clone())).await?;

    // Shared control state: a std Mutex (never held across .await) + a Notify
    // so commands from the reader task can wake the replay loop immediately.
    let control = std::sync::Arc::new(std::sync::Mutex::new(crate::control::Control::default()));
    let notify = std::sync::Arc::new(tokio::sync::Notify::new());

    // Reader task: parse inbound text messages and apply commands.
    {
        let (rc, rn) = (control.clone(), notify.clone());
        tokio::spawn(async move {
            while let Some(Ok(msg)) = read.next().await {
                if let Message::Text(t) = msg {
                    if let Some(cmd) = crate::control::parse_command(&t) {
                        rc.lock().unwrap().apply(&cmd);
                        rn.notify_one();
                    }
                }
            }
        });
    }

    let pacer = Pacer::new(cfg.speed);
    let mut sampler = MetricsSampler::new();
    let base = Instant::now();
    // Monotonic wall-clock milliseconds since the connection started.
    let now_ms = || base.elapsed().as_millis() as i64;
    // Track the last whole-second boundary (in replay time) at which metrics were emitted.
    let mut last_metric_sec: i64 = -1;
    // t0: wall-clock offset that defines the replay clock.
    // effective_elapsed = now_ms() - t0; a sample at ts_ms is due when effective_elapsed >= ts_ms/speed.
    let mut t0: i64 = 0;
    let mut i: usize = 0;

    while i < samples.len() {
        // ── Read control state ────────────────────────────────────────────────
        // Lock briefly to copy out seek/paused; drop the guard before any .await.
        let (seek, paused) = {
            let mut c = control.lock().unwrap();
            (c.seek_to.take(), c.paused)
        };

        // ── Handle seek ───────────────────────────────────────────────────────
        if let Some(target) = seek {
            // Jump to the first sample with ts_ms >= target.
            i = samples.partition_point(|s| s.ts_ms < target);
            // Re-send meta so the client knows the stream has reset.
            write.send(Message::Text(meta_json.clone())).await?;
            // Rebase the replay clock so `target` is due right now.
            t0 = pacer.rebase_for_seek(now_ms(), target);
            continue; // re-check control (e.g. paused was also set)
        }

        // ── Handle pause ──────────────────────────────────────────────────────
        if paused {
            let pause_start = now_ms();
            // Wait until any command arrives, then re-check paused.
            // notify_one() stores a permit even if no task is waiting, so a resume/seek that fires before we reach notified().await is not lost.
            loop {
                notify.notified().await;
                if !control.lock().unwrap().paused {
                    break;
                }
            }
            // Shift t0 forward by the paused duration so the replay clock
            // doesn't drift; the next sample will be due at the same relative
            // offset as before the pause.
            t0 = pacer.rebase_for_pause(t0, now_ms() - pause_start);
            continue; // re-check control; a seek might have arrived during pause
        }

        // ── Wait for the current sample to be due, interruptibly ──────────────
        let wait = pacer.wait_ms(samples[i].ts_ms, now_ms() - t0);
        if wait > 0 {
            tokio::select! {
                // Normal path: sample becomes due.
                _ = tokio::time::sleep(std::time::Duration::from_millis(wait as u64)) => {}
                // Command arrived during sleep: re-check control WITHOUT emitting the frame.
                _ = notify.notified() => { continue; }
            }
        }

        // ── Emit frame ────────────────────────────────────────────────────────
        let s = &samples[i];
        let frame = FrameMessage::new(s.ts_ms, now_unix_ms(), s.values.clone());
        write
            .send(Message::Text(serde_json::to_string(&frame)?))
            .await?;

        // Emit metrics once per replay-second (keyed on replay time, not wall time).
        let sec = s.ts_ms / 1000;
        if sec != last_metric_sec {
            last_metric_sec = sec;
            let m = sampler.sample();
            write
                .send(Message::Text(
                    serde_json::to_string(&MetricsMessage::new(m.cpu_pct, m.ram_mb))?,
                ))
                .await?;
        }

        i += 1;
    }

    // Send a proper WS close frame so the client sees end-of-stream.
    // (Without this, the reader task keeps the underlying connection open and
    // the client never receives an EOF or close notification.)
    let _ = write.send(Message::Close(None)).await;

    Ok(())
}
