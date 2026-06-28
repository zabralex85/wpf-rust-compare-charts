use std::net::SocketAddr;

#[tokio::test]
async fn serves_tiles_tilejson_and_204() {
    let fix = concat!(env!("CARGO_MANIFEST_DIR"), "/../../tiles/fixture.mbtiles").to_string();
    let addr: SocketAddr = "127.0.0.1:0".parse().unwrap();
    let listener = tokio::net::TcpListener::bind(addr).await.unwrap();
    let bound = listener.local_addr().unwrap();
    tokio::spawn(async move {
        app_lib::tiles::serve_with_listener(listener, Some(fix), None)
            .await
            .unwrap();
    });
    // give it a tick
    tokio::time::sleep(std::time::Duration::from_millis(50)).await;
    let base = format!("http://{}", bound);

    let tj = reqwest::get(format!("{}/tiles.json", base)).await.unwrap();
    assert_eq!(tj.status(), 200);
    assert!(tj.text().await.unwrap().contains("vector_layers"));

    // present tile (XYZ z1/x0/y1 ↔ TMS z1/x0/y0)
    let t = reqwest::get(format!("{}/tiles/1/0/1.pbf", base)).await.unwrap();
    assert_eq!(t.status(), 200);
    assert_eq!(t.headers().get("content-encoding").unwrap(), "gzip");

    // absent tile → 204
    let miss = reqwest::get(format!("{}/tiles/1/5/5.pbf", base)).await.unwrap();
    assert_eq!(miss.status(), 204);
}
