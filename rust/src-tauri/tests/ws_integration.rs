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

    // then at least a few frames, ts increasing
    let mut last_ts = -1i64;
    let mut frames = 0;
    while frames < 5 {
        let msg = ws.next().await.unwrap().unwrap();
        let v: serde_json::Value = serde_json::from_str(msg.to_text().unwrap()).unwrap();
        if v["type"] == "frame" {
            let ts = v["ts_ms"].as_i64().unwrap();
            assert!(ts > last_ts);
            last_ts = ts;
            frames += 1;
        }
    }
}
