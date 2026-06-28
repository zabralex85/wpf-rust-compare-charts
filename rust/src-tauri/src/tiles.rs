use rusqlite::{Connection, OptionalExtension};
use std::collections::HashMap;

pub struct MbTiles {
    conn: Connection,
}

impl MbTiles {
    pub fn open(path: &str) -> anyhow::Result<MbTiles> {
        let conn = Connection::open_with_flags(path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)?;
        Ok(MbTiles { conn })
    }

    pub fn tile_xyz(&self, z: u32, x: u32, y: u32) -> rusqlite::Result<Option<Vec<u8>>> {
        let row = (1u32 << z).wrapping_sub(1).wrapping_sub(y); // XYZ → TMS
        self.conn
            .query_row(
                "SELECT tile_data FROM tiles WHERE zoom_level=?1 AND tile_column=?2 AND tile_row=?3",
                rusqlite::params![z, x, row],
                |r| r.get::<_, Vec<u8>>(0),
            )
            .optional()
    }

    pub fn metadata(&self) -> rusqlite::Result<HashMap<String, String>> {
        let mut stmt = self.conn.prepare("SELECT name, value FROM metadata")?;
        let rows = stmt.query_map([], |r| Ok((r.get::<_, String>(0)?, r.get::<_, String>(1)?)))?;
        rows.collect()
    }

    pub fn tilejson(&self, tiles_url: &str) -> serde_json::Value {
        // DB error yields empty metadata; this is infallible
        let meta = self.metadata().unwrap_or_default();
        let num = |k: &str, d: f64| meta.get(k).and_then(|v| v.parse::<f64>().ok()).unwrap_or(d);
        let bounds: Vec<f64> = meta
            .get("bounds")
            .map(|b| b.split(',').filter_map(|s| s.parse().ok()).collect())
            .unwrap_or_else(|| vec![-180.0, -85.0, 180.0, 85.0]);
        let minzoom = num("minzoom", 0.0) as u32;
        let center = meta
            .get("center")
            .and_then(|c| {
                let parts: Vec<f64> = c.split(',').filter_map(|s| s.trim().parse().ok()).collect();
                if parts.len() == 3 {
                    Some(serde_json::json!([parts[0], parts[1], parts[2]]))
                } else {
                    None
                }
            })
            .unwrap_or_else(|| {
                // Default to bounds midpoint with minzoom
                serde_json::json!([(bounds[0] + bounds[2]) / 2.0, (bounds[1] + bounds[3]) / 2.0, minzoom])
            });
        let vector_layers = meta
            .get("json")
            .and_then(|j| serde_json::from_str::<serde_json::Value>(j).ok())
            .and_then(|v| v.get("vector_layers").cloned())
            .unwrap_or(serde_json::json!([]));
        serde_json::json!({
            "tilejson": "2.2.0",
            "tiles": [tiles_url],
            "minzoom": minzoom,
            "maxzoom": num("maxzoom", 14.0) as u32,
            "bounds": bounds,
            "center": center,
            "vector_layers": vector_layers,
        })
    }
}

use axum::{
    extract::{Path, State},
    http::{header, StatusCode},
    response::IntoResponse,
    routing::get,
    Router,
};
use std::sync::{Arc, Mutex};

#[derive(Clone)]
struct AppState {
    mbtiles: Option<Arc<Mutex<MbTiles>>>,
    glyphs: Option<String>,
    tile_base: String,
}

pub async fn serve(
    addr: std::net::SocketAddr,
    mbtiles: Option<String>,
    glyphs: Option<String>,
) -> anyhow::Result<()> {
    let listener = tokio::net::TcpListener::bind(addr).await?;
    serve_with_listener(listener, mbtiles, glyphs).await
}

pub async fn serve_with_listener(
    listener: tokio::net::TcpListener,
    mbtiles: Option<String>,
    glyphs: Option<String>,
) -> anyhow::Result<()> {
    let mb = match mbtiles {
        Some(p) => MbTiles::open(&p).ok().map(|m| Arc::new(Mutex::new(m))),
        None => None,
    };
    let local = listener.local_addr()?;
    let tile_base = format!("http://{}", local);
    let state = AppState { mbtiles: mb, glyphs, tile_base };
    let app = Router::new()
        .route("/tiles.json", get(tilejson_handler))
        .route("/tiles/:z/:x/:y", get(tile_handler))
        .route("/glyphs/:fontstack/:range", get(glyph_handler))
        .with_state(state);
    axum::serve(listener, app).await?;
    Ok(())
}

async fn tilejson_handler(State(s): State<AppState>) -> impl IntoResponse {
    match &s.mbtiles {
        Some(m) => {
            let tiles_url = format!("{}/tiles/{{z}}/{{x}}/{{y}}.pbf", s.tile_base);
            let tj = m
                .lock()
                .unwrap()
                .tilejson(&tiles_url);
            axum::Json(tj).into_response()
        }
        None => axum::Json(serde_json::json!({})).into_response(),
    }
}

async fn tile_handler(
    State(s): State<AppState>,
    Path((z, x, y)): Path<(u32, u32, String)>,
) -> impl IntoResponse {
    let y = y.trim_end_matches(".pbf").parse::<u32>().unwrap_or(u32::MAX);
    let blob = s
        .mbtiles
        .as_ref()
        .and_then(|m| m.lock().unwrap().tile_xyz(z, x, y).ok().flatten());
    match blob {
        Some(bytes) => (
            StatusCode::OK,
            [
                (header::CONTENT_TYPE, "application/x-protobuf"),
                (header::CONTENT_ENCODING, "gzip"),
            ],
            bytes,
        )
            .into_response(),
        None => StatusCode::NO_CONTENT.into_response(),
    }
}

/// Whitelist guard for glyph path segments.
///
/// Accepts only non-empty strings whose every character is ASCII alphanumeric,
/// space, underscore, hyphen, or dot — AND that do not contain "..".
/// Rejects backslashes, forward slashes, percent signs, and every other
/// character that could be used for path traversal (including URL-decoded
/// variants such as `\` from `%5c` and `/` from `%2f`).
pub(crate) fn safe_seg(s: &str) -> bool {
    !s.is_empty()
        && s.chars()
            .all(|c| c.is_ascii_alphanumeric() || matches!(c, ' ' | '_' | '-' | '.'))
        && !s.contains("..")
}

async fn glyph_handler(
    State(s): State<AppState>,
    Path((fontstack, range)): Path<(String, String)>,
) -> impl IntoResponse {
    let dir = match &s.glyphs {
        Some(d) => d,
        None => return StatusCode::NOT_FOUND.into_response(),
    };
    if !safe_seg(&fontstack) || !safe_seg(&range) {
        return StatusCode::NOT_FOUND.into_response();
    }
    let path = std::path::Path::new(dir).join(&fontstack).join(&range);
    match tokio::fs::read(&path).await {
        Ok(bytes) => (
            StatusCode::OK,
            [(header::CONTENT_TYPE, "application/x-protobuf")],
            bytes,
        )
            .into_response(),
        Err(_) => StatusCode::NOT_FOUND.into_response(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    const FIX: &str = concat!(env!("CARGO_MANIFEST_DIR"), "/../../tiles/fixture.mbtiles");

    // --- safe_seg whitelist ---

    #[test]
    fn safe_seg_accepts_valid_fontstack_and_range() {
        assert!(safe_seg("Noto Sans Regular"));
        assert!(safe_seg("0-255.pbf"));
        assert!(safe_seg("Open_Sans-Bold"));
        assert!(safe_seg("a"));
    }

    #[test]
    fn safe_seg_rejects_dotdot() {
        assert!(!safe_seg(".."));
        assert!(!safe_seg("../etc"));
        assert!(!safe_seg("foo..bar"));
    }

    #[test]
    fn safe_seg_rejects_slash() {
        assert!(!safe_seg("a/b"));
        assert!(!safe_seg("/etc/passwd"));
    }

    #[test]
    fn safe_seg_rejects_backslash() {
        // Windows path-traversal vector: backslash causes Path::join to treat
        // the segment as drive-relative on Windows.
        assert!(!safe_seg(r"\windows\system32"));
        assert!(!safe_seg(r"a\b"));
    }

    #[test]
    fn safe_seg_rejects_percent() {
        // URL-encoded traversal (%2f = '/', %5c = '\') must be rejected even
        // after axum's URL-decoding, because '%' itself is not in the whitelist.
        assert!(!safe_seg("%2f"));
        assert!(!safe_seg("%5c"));
    }

    #[test]
    fn safe_seg_rejects_empty() {
        assert!(!safe_seg(""));
    }

    #[test]
    fn reads_present_tile_with_yflip() {
        let m = MbTiles::open(FIX).unwrap();
        // fixture has TMS (z1,x0,y0). XYZ y for that is (1<<1)-1-0 = 1.
        assert!(m.tile_xyz(1, 0, 1).unwrap().is_some());
        // absent tile
        assert!(m.tile_xyz(1, 5, 5).unwrap().is_none());
    }

    #[test]
    fn metadata_and_tilejson() {
        let m = MbTiles::open(FIX).unwrap();
        let meta = m.metadata().unwrap();
        assert_eq!(meta.get("format").map(String::as_str), Some("pbf"));
        let tj = m.tilejson("http://127.0.0.1:9002/tiles/{z}/{x}/{y}.pbf");
        assert_eq!(tj["tiles"][0], "http://127.0.0.1:9002/tiles/{z}/{x}/{y}.pbf");
        assert_eq!(tj["maxzoom"], 1);
        assert!(tj["vector_layers"].is_array());
        assert!(tj["center"].is_array());
        assert_eq!(tj["center"].as_array().unwrap().len(), 3);
    }
}
