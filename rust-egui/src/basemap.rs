//! Minimal offline MVT basemap: read a gzipped Mapbox Vector Tile from the
//! `israel.mbtiles` (via app_lib's MbTiles reader), decode it, and expose the
//! water/road geometries in tile-normalized [0,1] coords for the egui painter.

use app_lib::tiles::MbTiles;
use flate2::read::GzDecoder;
use std::io::Read;

use prost::Message;

/// A decoded polyline/ring in tile-normalized [0,1] coordinates (y down).
pub type Shape = Vec<(f32, f32)>;

pub struct TileGeom {
    pub water: Vec<Shape>,
    pub roads: Vec<Shape>,
}

/// Basemap geometry for a region, in (lat, lon) so it projects with the track.
pub struct Region {
    pub water: Vec<Vec<(f64, f64)>>,
    pub roads: Vec<Vec<(f64, f64)>>,
}

// ---- web-mercator tile math (XYZ) ----
fn lon2tx(lon: f64, z: u32) -> f64 {
    (lon + 180.0) / 360.0 * (1u32 << z) as f64
}
fn lat2ty(lat: f64, z: u32) -> f64 {
    let r = lat.to_radians();
    (1.0 - (r.tan() + 1.0 / r.cos()).ln() / std::f64::consts::PI) / 2.0 * (1u32 << z) as f64
}
fn tx2lon(x: f64, z: u32) -> f64 {
    x / (1u32 << z) as f64 * 360.0 - 180.0
}
fn ty2lat(y: f64, z: u32) -> f64 {
    let n = std::f64::consts::PI * (1.0 - 2.0 * y / (1u32 << z) as f64);
    n.sinh().atan().to_degrees()
}

/// Load water + road geometry for a lat/lon bounding box, projecting each tile's
/// features back to (lat, lon). Returns empty region if the tileset is missing.
pub fn load_region(mb: &MbTiles, min_lat: f64, max_lat: f64, min_lon: f64, max_lon: f64) -> Region {
    let mut region = Region { water: Vec::new(), roads: Vec::new() };
    let span = (max_lon - min_lon).abs().max(1e-6);
    // pick a zoom that puts ~4 tiles across the bbox, clamped to a sane range
    let z = ((4.0 * 360.0 / span).log2().floor() as i32).clamp(10, 14) as u32;
    let (x0, x1) = (lon2tx(min_lon, z).floor() as i64, lon2tx(max_lon, z).floor() as i64);
    let (y0, y1) = (lat2ty(max_lat, z).floor() as i64, lat2ty(min_lat, z).floor() as i64);
    if (x1 - x0 + 1) * (y1 - y0 + 1) > 64 {
        return region; // safety cap
    }
    for tx in x0..=x1 {
        for ty in y0..=y1 {
            if tx < 0 || ty < 0 {
                continue;
            }
            if let Ok(Some(blob)) = mb.tile_xyz(z, tx as u32, ty as u32) {
                if let Some(g) = decode(&blob) {
                    for s in &g.water {
                        region.water.push(to_latlon(s, tx as f64, ty as f64, z));
                    }
                    for s in &g.roads {
                        region.roads.push(to_latlon(s, tx as f64, ty as f64, z));
                    }
                }
            }
        }
    }
    region
}

fn to_latlon(shape: &Shape, tx: f64, ty: f64, z: u32) -> Vec<(f64, f64)> {
    shape
        .iter()
        .map(|&(u, v)| (ty2lat(ty + v as f64, z), tx2lon(tx + u as f64, z)))
        .collect()
}

/// gunzip + prost-decode an MVT tile, returning water polygons + road lines
/// projected to tile-normalized [0,1] coords.
pub fn decode(gz: &[u8]) -> Option<TileGeom> {
    let mut dec = GzDecoder::new(gz);
    let mut buf = Vec::new();
    dec.read_to_end(&mut buf).ok()?;
    let tile = geozero::mvt::Tile::decode(&buf[..]).ok()?;
    let mut water = Vec::new();
    let mut roads = Vec::new();
    for layer in &tile.layers {
        let extent = layer.extent.unwrap_or(4096).max(1) as f32;
        let is_water = layer.name == "water";
        let is_road = layer.name == "transportation" || layer.name == "road";
        if !is_water && !is_road {
            continue;
        }
        for feat in &layer.features {
            for ring in decode_geometry(&feat.geometry) {
                let shape: Shape = ring.into_iter().map(|(x, y)| (x as f32 / extent, y as f32 / extent)).collect();
                if shape.len() >= 2 {
                    if is_water {
                        water.push(shape);
                    } else {
                        roads.push(shape);
                    }
                }
            }
        }
    }
    Some(TileGeom { water, roads })
}

/// Decode MVT command-encoded geometry into sub-paths of (x,y) tile coords.
fn decode_geometry(cmds: &[u32]) -> Vec<Vec<(i32, i32)>> {
    let mut paths = Vec::new();
    let mut cur: Vec<(i32, i32)> = Vec::new();
    let (mut cx, mut cy) = (0i32, 0i32);
    let mut i = 0;
    while i < cmds.len() {
        let cmd_int = cmds[i];
        i += 1;
        let id = cmd_int & 0x7;
        let count = cmd_int >> 3;
        match id {
            1 => {
                // MoveTo: starts a new sub-path
                for _ in 0..count {
                    if i + 1 >= cmds.len() {
                        break;
                    }
                    cx += zigzag(cmds[i]);
                    cy += zigzag(cmds[i + 1]);
                    i += 2;
                    if !cur.is_empty() {
                        paths.push(std::mem::take(&mut cur));
                    }
                    cur.push((cx, cy));
                }
            }
            2 => {
                // LineTo
                for _ in 0..count {
                    if i + 1 >= cmds.len() {
                        break;
                    }
                    cx += zigzag(cmds[i]);
                    cy += zigzag(cmds[i + 1]);
                    i += 2;
                    cur.push((cx, cy));
                }
            }
            7 => {
                // ClosePath: close the current ring
                if let Some(&first) = cur.first() {
                    cur.push(first);
                }
            }
            _ => break,
        }
    }
    if !cur.is_empty() {
        paths.push(cur);
    }
    paths
}

fn zigzag(v: u32) -> i32 {
    ((v >> 1) as i32) ^ -((v & 1) as i32)
}

#[cfg(test)]
mod tests {
    use super::*;
    use app_lib::tiles::MbTiles;

    #[test]
    fn decodes_tel_aviv_tile() {
        let mb = MbTiles::open("../tiles/israel.mbtiles").expect("open mbtiles");
        let blob = mb.tile_xyz(12, 2443, 1661).expect("query").expect("tile exists at z12/2443/1661");
        let g = decode(&blob).expect("decode");
        println!("water rings={} road lines={}", g.water.len(), g.roads.len());
        assert!(g.water.len() + g.roads.len() > 0, "expected some water/road geometry");
    }
}
