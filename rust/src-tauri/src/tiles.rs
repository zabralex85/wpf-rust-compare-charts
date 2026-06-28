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
        let meta = self.metadata().unwrap_or_default();
        let num = |k: &str, d: f64| meta.get(k).and_then(|v| v.parse::<f64>().ok()).unwrap_or(d);
        let bounds: Vec<f64> = meta
            .get("bounds")
            .map(|b| b.split(',').filter_map(|s| s.parse().ok()).collect())
            .unwrap_or_else(|| vec![-180.0, -85.0, 180.0, 85.0]);
        let vector_layers = meta
            .get("json")
            .and_then(|j| serde_json::from_str::<serde_json::Value>(j).ok())
            .and_then(|v| v.get("vector_layers").cloned())
            .unwrap_or(serde_json::json!([]));
        serde_json::json!({
            "tilejson": "2.2.0",
            "tiles": [tiles_url],
            "minzoom": num("minzoom", 0.0) as u32,
            "maxzoom": num("maxzoom", 14.0) as u32,
            "bounds": bounds,
            "vector_layers": vector_layers,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    const FIX: &str = concat!(env!("CARGO_MANIFEST_DIR"), "/../../tiles/fixture.mbtiles");

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
    }
}
