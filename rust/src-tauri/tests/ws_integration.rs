use futures_util::StreamExt;
use tokio_tungstenite::connect_async;

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
