use std::net::SocketAddr;

/// Verify that glyph requests with path-traversal segments in either
/// `fontstack` or `range` are rejected with 404.
///
/// The glyphs dir is set to a known-present directory (the crate root) so
/// that a *valid* request would succeed, proving the server is actually live
/// and that the 404 responses come from the guard, not from the server being
/// unreachable.
#[tokio::test]
async fn glyph_handler_rejects_path_traversal() {
    let glyphs_dir = env!("CARGO_MANIFEST_DIR").to_string();
    let addr: SocketAddr = "127.0.0.1:0".parse().unwrap();
    let listener = tokio::net::TcpListener::bind(addr).await.unwrap();
    let bound = listener.local_addr().unwrap();
    tokio::spawn(async move {
        app_lib::tiles::serve_with_listener(listener, None, Some(glyphs_dir))
            .await
            .unwrap();
    });
    tokio::time::sleep(std::time::Duration::from_millis(50)).await;
    let base = format!("http://{}", bound);

    // dotdot in fontstack (URL-encoded as %2f to sneak through path routers)
    let r = reqwest::get(format!("{}/glyphs/..%2f../x/0-255.pbf", base))
        .await
        .unwrap();
    assert_eq!(r.status(), 404, "dotdot in fontstack must be 404");

    // backslash in fontstack (URL-encoded %5c — Windows path traversal vector)
    let r = reqwest::get(format!("{}/glyphs/%5cwindows/0-255.pbf", base))
        .await
        .unwrap();
    assert_eq!(r.status(), 404, "backslash in fontstack must be 404");

    // dotdot in range
    let r = reqwest::get(format!("{}/glyphs/Noto Sans Regular/..%2fetc", base))
        .await
        .unwrap();
    assert_eq!(r.status(), 404, "dotdot in range must be 404");

    // valid-looking request goes through guard (returns 404 only because no
    // actual .pbf file exists at that path — proves guard passed it through)
    let r = reqwest::get(format!("{}/glyphs/Noto Sans Regular/0-255.pbf", base))
        .await
        .unwrap();
    assert_eq!(r.status(), 404, "valid segment should reach fs-read (404 = file not found, not guard)");
}

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
    assert_eq!(t.headers().get("content-type").unwrap(), "application/x-protobuf");

    // absent tile → 204
    let miss = reqwest::get(format!("{}/tiles/1/5/5.pbf", base)).await.unwrap();
    assert_eq!(miss.status(), 204);
}
