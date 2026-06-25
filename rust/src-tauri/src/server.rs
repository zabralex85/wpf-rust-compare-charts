use std::time::{Instant, SystemTime, UNIX_EPOCH};

use futures_util::SinkExt;
use rusqlite::Connection;
use tokio::net::TcpListener;
use tokio_tungstenite::tungstenite::Message;

use crate::db::{load_channels, load_enum_values, load_samples};
use crate::frame::{FrameMessage, MetaMessage};
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
    let mut ws = tokio_tungstenite::accept_async(stream).await?;

    // Load DB on a blocking thread (rusqlite is sync).
    let db_path = cfg.db_path.clone();
    let (meta_json, samples, _rate) = tokio::task::spawn_blocking(move || {
        let conn = Connection::open(&db_path)?;
        let channels = load_channels(&conn)?;
        let enums = load_enum_values(&conn)?;
        let rate: i64 =
            conn.query_row("SELECT rate_hz FROM ride_meta", [], |r| r.get(0))?;
        let samples = load_samples(&conn, &channels)?;
        let meta = MetaMessage::new(channels, enums, rate);
        Ok::<_, rusqlite::Error>((serde_json::to_string(&meta).unwrap(), samples, rate))
    })
    .await??;

    ws.send(Message::Text(meta_json)).await?;

    let pacer = Pacer::new(cfg.speed);
    let mut sampler = MetricsSampler::new();
    let start = Instant::now();
    let mut last_metrics = Instant::now();

    for s in samples {
        let elapsed = start.elapsed().as_millis() as i64;
        let wait = pacer.wait_ms(s.ts_ms, elapsed);
        if wait > 0 {
            tokio::time::sleep(std::time::Duration::from_millis(wait as u64)).await;
        }
        let frame = FrameMessage::new(s.ts_ms, now_unix_ms(), s.values);
        ws.send(Message::Text(serde_json::to_string(&frame)?))
            .await?;

        if last_metrics.elapsed().as_millis() >= 1000 {
            last_metrics = Instant::now();
            let m = sampler.sample();
            let mj = serde_json::json!({
                "type": "metrics",
                "cpu_pct": m.cpu_pct,
                "ram_mb": m.ram_mb
            });
            ws.send(Message::Text(mj.to_string())).await?;
        }
    }
    Ok(())
}
