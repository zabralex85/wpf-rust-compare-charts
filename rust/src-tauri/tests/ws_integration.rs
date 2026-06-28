use std::time::Duration;

use futures_util::{SinkExt, StreamExt};
use tokio_tungstenite::{connect_async, tungstenite::Message};

/// Concrete WS client type returned by `connect_async`.
type WsClient = tokio_tungstenite::WebSocketStream<
    tokio_tungstenite::MaybeTlsStream<tokio::net::TcpStream>,
>;

/// Reads until a `"frame"` message arrives, discarding everything else.
/// Panics if the server closes the stream first.
async fn next_frame(ws: &mut WsClient) -> serde_json::Value {
    loop {
        let msg = ws.next().await.expect("stream ended").expect("ws error");
        if msg.is_close() {
            panic!("server closed ws during next_frame");
        }
        let Ok(text) = msg.to_text() else { continue };
        let v: serde_json::Value = serde_json::from_str(text).unwrap();
        if v["type"] == "frame" {
            return v;
        }
    }
}

/// Reads until a message of `msg_type` arrives, discarding everything else.
async fn next_msg_of_type(ws: &mut WsClient, msg_type: &str) -> serde_json::Value {
    loop {
        let msg = ws.next().await.expect("stream ended").expect("ws error");
        if msg.is_close() {
            panic!("server closed ws during next_msg_of_type({msg_type})");
        }
        let Ok(text) = msg.to_text() else { continue };
        let v: serde_json::Value = serde_json::from_str(text).unwrap();
        if v["type"].as_str() == Some(msg_type) {
            return v;
        }
    }
}

#[tokio::test]
async fn client_receives_meta_then_frames() {
    // ephemeral port
    let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
    let addr = listener.local_addr().unwrap();
    let cfg = app_lib::server::ServerConfig {
        db_path: "../../data/ride_small.db".into(),
        port: addr.port(),
        speed: 1000.0, // fast-forward so the 10s ride streams quickly
    };
    tokio::spawn(async move { app_lib::server::serve_on(listener, cfg).await });

    let url = format!("ws://{}", addr);
    let (mut ws, _) = connect_async(&url).await.expect("connect");

    // first message must be meta
    let first = ws.next().await.unwrap().unwrap();
    let v: serde_json::Value = serde_json::from_str(first.to_text().unwrap()).unwrap();
    assert_eq!(v["type"], "meta");
    assert_eq!(v["channels"].as_array().unwrap().len(), 30);
    assert!(v["rate_hz"].as_i64().unwrap() >= 1);
    assert!(v["duration_s"].as_i64().unwrap() >= 1); // ride length present

    // collect all messages until the server closes the stream
    let mut last_ts = -1i64;
    let mut frame_count = 0usize;
    let mut metrics_count = 0usize;
    let mut last_metrics: Option<(f32, f64)> = None; // (cpu_pct, ram_mb)

    while let Some(Ok(msg)) = ws.next().await {
        if msg.is_close() {
            break;
        }
        let text = match msg.to_text() {
            Ok(t) => t,
            Err(_) => continue,
        };
        let v: serde_json::Value = serde_json::from_str(text).unwrap();
        match v["type"].as_str().unwrap_or("") {
            "frame" => {
                let ts = v["ts_ms"].as_i64().unwrap();
                assert!(
                    ts > last_ts,
                    "ts must be strictly increasing: {} <= {}",
                    ts,
                    last_ts
                );
                last_ts = ts;
                assert_eq!(v["values"].as_array().unwrap().len(), 30);
                assert!(v["emit_unix_ms"].as_i64().unwrap() > 0);
                frame_count += 1;
            }
            "metrics" => {
                let cpu = v["cpu_pct"].as_f64().unwrap() as f32;
                let ram = v["ram_mb"].as_f64().unwrap();
                last_metrics = Some((cpu, ram));
                metrics_count += 1;
            }
            _ => {}
        }
    }

    assert!(frame_count >= 5, "expected at least 5 frames, got {}", frame_count);
    assert!(
        metrics_count >= 1,
        "expected at least 1 metrics message, got {}",
        metrics_count
    );
    let (cpu_pct, ram_mb) = last_metrics.unwrap();
    assert!(
        cpu_pct >= 0.0 && cpu_pct.is_finite(),
        "cpu_pct should be finite and >= 0, got {}",
        cpu_pct
    );
    assert!(ram_mb > 0.0, "ram_mb should be positive, got {}", ram_mb);
}

#[tokio::test]
async fn pause_resume_seek_commands() {
    // Speed=10: frames arrive every ~10ms wall-time; the 10s ride lasts ~1s total.
    // That gives enough margin for the 300ms pause check + 500ms resume check + seek.
    let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
    let addr = listener.local_addr().unwrap();
    let cfg = app_lib::server::ServerConfig {
        db_path: "../../data/ride_small.db".into(),
        port: addr.port(),
        speed: 10.0,
    };
    tokio::spawn(async move { app_lib::server::serve_on(listener, cfg).await });

    let url = format!("ws://{}", addr);
    let (mut ws, _) = connect_async(&url).await.expect("connect");

    // First message must be meta.
    let first = ws.next().await.unwrap().unwrap();
    let v: serde_json::Value = serde_json::from_str(first.to_text().unwrap()).unwrap();
    assert_eq!(v["type"], "meta", "first message must be meta");

    // Receive at least one frame to confirm streaming is live.
    let _first_frame = next_frame(&mut ws).await;

    // ── Pause ──────────────────────────────────────────────────────────────────
    ws.send(Message::Text(
        r#"{"type":"cmd","action":"pause"}"#.into(),
    ))
    .await
    .unwrap();

    // Drain frames that were already in-flight before pause was applied.
    // At speed=10 the inter-frame time is ~10ms; a 60ms window covers ≤6 in-flight frames.
    let drain_end = tokio::time::Instant::now() + Duration::from_millis(60);
    loop {
        let remaining = drain_end.saturating_duration_since(tokio::time::Instant::now());
        if remaining.is_zero() {
            break;
        }
        if tokio::time::timeout(remaining, ws.next()).await.is_err() {
            break;
        }
    }

    // No frame must arrive for 300ms while paused.
    let paused_result =
        tokio::time::timeout(Duration::from_millis(300), next_frame(&mut ws)).await;
    assert!(paused_result.is_err(), "frames must stop while paused");

    // ── Resume ─────────────────────────────────────────────────────────────────
    ws.send(Message::Text(
        r#"{"type":"cmd","action":"resume"}"#.into(),
    ))
    .await
    .unwrap();
    let resumed_result =
        tokio::time::timeout(Duration::from_millis(700), next_frame(&mut ws)).await;
    assert!(resumed_result.is_ok(), "a frame must arrive within 700ms of resume");

    // ── Seek to 8000ms ─────────────────────────────────────────────────────────
    ws.send(Message::Text(
        r#"{"type":"cmd","action":"seek","ts_ms":8000}"#.into(),
    ))
    .await
    .unwrap();

    // Server re-sends meta after seek (skipping any frames from the old position).
    let meta_after_seek =
        tokio::time::timeout(Duration::from_millis(500), next_msg_of_type(&mut ws, "meta"))
            .await
            .expect("meta must arrive within 500ms of seek");
    assert_eq!(meta_after_seek["type"], "meta");

    // Then a frame with ts_ms >= 8000 must follow.
    let frame_after_seek =
        tokio::time::timeout(Duration::from_millis(500), next_frame(&mut ws))
            .await
            .expect("frame must arrive within 500ms of seek");
    let ts = frame_after_seek["ts_ms"].as_i64().expect("ts_ms present");
    assert!(ts >= 8000, "frame after seek must have ts >= 8000, got {ts}");
}
