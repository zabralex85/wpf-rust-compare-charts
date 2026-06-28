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
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![greet])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
