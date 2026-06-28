pub mod db;
pub mod frame;
pub mod metrics;
pub mod replay;
pub mod server;
pub mod tiles;

// Learn more about Tauri commands at https://tauri.app/develop/calling-rust/
#[tauri::command]
fn greet(name: &str) -> String {
    format!("Hello, {}! You've been greeted from Rust!", name)
}

/// Auto-resolve the MBTiles path: prefer the full Israel build, fall back to the
/// committed test fixture, else None (the tile server then serves 204s).
fn resolve_default_mbtiles() -> Option<String> {
    for cand in ["../../tiles/israel.mbtiles", "../../tiles/fixture.mbtiles"] {
        if std::path::Path::new(cand).exists() {
            return Some(cand.to_string());
        }
    }
    None
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .setup(|_app| {
            let port: u16 = std::env::var("RIDE_WS_PORT")
                .ok()
                .and_then(|s| s.parse().ok())
                .unwrap_or(9001);
            let db_path =
                std::env::var("RIDE_DB").unwrap_or_else(|_| "../../data/ride.db".into());
            let speed: f64 = std::env::var("RIDE_SPEED")
                .ok()
                .and_then(|s| s.parse().ok())
                .unwrap_or(1.0);
            tauri::async_runtime::spawn(async move {
                let cfg = crate::server::ServerConfig {
                    db_path,
                    port,
                    speed,
                };
                if let Err(e) = crate::server::serve(cfg).await {
                    eprintln!("ws server error: {e}");
                }
            });

            // Offline vector-tile + glyph HTTP server (MapLibre basemap)
            let tiles_port: u16 = std::env::var("RIDE_TILES_PORT")
                .ok()
                .and_then(|s| s.parse().ok())
                .unwrap_or(9002);
            let mbtiles = std::env::var("RIDE_MBTILES")
                .ok()
                .or_else(resolve_default_mbtiles);
            let glyphs = std::env::var("RIDE_GLYPHS")
                .ok()
                .or_else(|| Some("../../tiles/glyphs".to_string()));
            tauri::async_runtime::spawn(async move {
                let addr = std::net::SocketAddr::from(([127, 0, 0, 1], tiles_port));
                if let Err(e) = crate::tiles::serve(addr, mbtiles, glyphs).await {
                    eprintln!("tile server error: {e}");
                }
            });
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![greet])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
