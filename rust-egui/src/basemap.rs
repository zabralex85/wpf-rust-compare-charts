//! Offline MVT basemap: decode Mapbox Vector Tiles from `israel.mbtiles` (via
//! app_lib's MbTiles reader) into filled polygons, class-ranked road lines, and
//! place labels — all in tile-normalized [0,1] coords — for an egui-painter
//! slippy map (pan/zoom). No wgpu, no WebView; reuses the glow GL context.

use flate2::read::GzDecoder;
use std::io::Read;

use prost::Message;

/// Polygon fill category (drives the colour).
#[derive(Clone, Copy, PartialEq)]
pub enum Fill {
    Water,
    Green,
    Land,
}

/// A tile decoded into paintable geometry (tile-normalized [0,1], y down).
pub struct Tile {
    pub polys: Vec<(Fill, Vec<(f32, f32)>)>,
    pub roads: Vec<(u8, Vec<(f32, f32)>)>, // rank 0=major .. 3=minor
    pub labels: Vec<(f32, f32, String)>,
}

/// gunzip + decode an MVT tile into fills / roads / labels.
pub fn decode(gz: &[u8]) -> Option<Tile> {
    let mut dec = GzDecoder::new(gz);
    let mut buf = Vec::new();
    dec.read_to_end(&mut buf).ok()?;
    let tile = geozero::mvt::Tile::decode(&buf[..]).ok()?;
    let mut out = Tile { polys: Vec::new(), roads: Vec::new(), labels: Vec::new() };
    for layer in &tile.layers {
        let extent = layer.extent.unwrap_or(4096).max(1) as f32;
        let name = layer.name.as_str();
        let keys = &layer.keys;
        let vals = &layer.values;
        for feat in &layer.features {
            let geoms = decode_geometry(&feat.geometry);
            match name {
                "water" => push_polys(&mut out.polys, Fill::Water, geoms, extent),
                "landcover" | "park" => push_polys(&mut out.polys, Fill::Green, geoms, extent),
                "landuse" => push_polys(&mut out.polys, Fill::Land, geoms, extent),
                "transportation" => {
                    let rank = road_rank(class_of(feat, keys, vals));
                    for g in geoms {
                        let s: Vec<(f32, f32)> = g.into_iter().map(|(x, y)| (x as f32 / extent, y as f32 / extent)).collect();
                        if s.len() >= 2 {
                            out.roads.push((rank, s));
                        }
                    }
                }
                "place" | "poi" | "transportation_name" => {
                    if let Some(txt) = name_of(feat, keys, vals) {
                        if let Some(&(x, y)) = geoms.first().and_then(|g| g.first()) {
                            out.labels.push((x as f32 / extent, y as f32 / extent, txt));
                        }
                    }
                }
                _ => {}
            }
        }
    }
    Some(out)
}

fn push_polys(dst: &mut Vec<(Fill, Vec<(f32, f32)>)>, fill: Fill, geoms: Vec<Vec<(i32, i32)>>, extent: f32) {
    for g in geoms {
        let s: Vec<(f32, f32)> = g.into_iter().map(|(x, y)| (x as f32 / extent, y as f32 / extent)).collect();
        if s.len() >= 3 {
            dst.push((fill, s));
        }
    }
}

/// Look up a feature's `class` string tag (OpenMapTiles road class).
fn class_of<'a>(feat: &geozero::mvt::tile::Feature, keys: &'a [String], vals: &'a [geozero::mvt::tile::Value]) -> &'a str {
    tag_str(feat, keys, vals, "class").unwrap_or("")
}

/// Look up a feature's `name:latin` tag. (Israel `name` is Hebrew — the default
/// font has no Hebrew glyphs, so latin-only avoids tofu boxes.)
fn name_of(feat: &geozero::mvt::tile::Feature, keys: &[String], vals: &[geozero::mvt::tile::Value]) -> Option<String> {
    tag_str(feat, keys, vals, "name:latin")
        .filter(|s| s.is_ascii())
        .map(|s| s.to_string())
}

fn tag_str<'a>(feat: &geozero::mvt::tile::Feature, keys: &'a [String], vals: &'a [geozero::mvt::tile::Value], key: &str) -> Option<&'a str> {
    let mut i = 0;
    while i + 1 < feat.tags.len() {
        let k = feat.tags[i] as usize;
        let v = feat.tags[i + 1] as usize;
        if keys.get(k).map(|s| s.as_str()) == Some(key) {
            return vals.get(v).and_then(|val| val.string_value.as_deref());
        }
        i += 2;
    }
    None
}

fn road_rank(class: &str) -> u8 {
    match class {
        "motorway" | "trunk" => 0,
        "primary" => 1,
        "secondary" | "tertiary" => 2,
        _ => 3,
    }
}

/// Decode MVT command-encoded geometry into sub-paths of (x,y) tile coords.
fn decode_geometry(cmds: &[u32]) -> Vec<Vec<(i32, i32)>> {
    let mut paths = Vec::new();
    let mut cur: Vec<(i32, i32)> = Vec::new();
    let (mut cx, mut cy) = (0i32, 0i32);
    let mut i = 0;
    while i < cmds.len() {
        let cmd = cmds[i];
        i += 1;
        let id = cmd & 0x7;
        let count = cmd >> 3;
        match id {
            1 => {
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

// ---- web-mercator projection (pixel space, 256 px tiles) ----
const TILE_PX: f64 = 256.0;
use std::f64::consts::PI;

/// (lon,lat) -> world pixel at fractional zoom `z`.
pub fn world(lon: f64, lat: f64, z: f64) -> (f64, f64) {
    let n = 2f64.powf(z) * TILE_PX;
    let x = (lon + 180.0) / 360.0 * n;
    let s = lat.to_radians().sin().clamp(-0.9999, 0.9999);
    let y = (0.5 - ((1.0 + s) / (1.0 - s)).ln() / (4.0 * PI)) * n;
    (x, y)
}

/// world pixel -> (lon,lat).
pub fn unworld(x: f64, y: f64, z: f64) -> (f64, f64) {
    let n = 2f64.powf(z) * TILE_PX;
    let lon = x / n * 360.0 - 180.0;
    let lat = (PI * (1.0 - 2.0 * y / n)).sinh().atan().to_degrees();
    (lon, lat)
}

/// A tile's fractional (col,row) -> lon/lat at integer zoom `z`.
pub fn tile_to_lonlat(tx: f64, ty: f64, z: u32) -> (f64, f64) {
    let n = (1u32 << z) as f64;
    let lon = tx / n * 360.0 - 180.0;
    let lat = (PI * (1.0 - 2.0 * ty / n)).sinh().atan().to_degrees();
    (lon, lat)
}

/// lon -> fractional tile column at zoom z.
pub fn lon_to_tx(lon: f64, z: u32) -> f64 {
    (lon + 180.0) / 360.0 * (1u32 << z) as f64
}
/// lat -> fractional tile row at zoom z.
pub fn lat_to_ty(lat: f64, z: u32) -> f64 {
    let r = lat.to_radians();
    (1.0 - (r.tan() + 1.0 / r.cos()).ln() / PI) / 2.0 * (1u32 << z) as f64
}

#[cfg(test)]
mod tests {
    use super::*;
    use app_lib::tiles::MbTiles;

    #[test]
    fn decodes_tel_aviv_tile() {
        let mb = MbTiles::open("../tiles/israel.mbtiles").expect("open mbtiles");
        let blob = mb.tile_xyz(12, 2443, 1661).expect("query").expect("tile at z12/2443/1661");
        let t = decode(&blob).expect("decode");
        assert!(t.polys.len() + t.roads.len() > 0);
    }
}
