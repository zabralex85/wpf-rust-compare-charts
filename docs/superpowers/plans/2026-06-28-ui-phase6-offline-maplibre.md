# UI Phase 6 — Offline MapLibre Vector Basemap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Serve an offline Israel vector basemap — built by tilemaker into MBTiles — from the Rust/Tauri backend over local HTTP, and render it in `MapWidget` with MapLibre GL plus the live GPS track. Remove Leaflet.

**Architecture:** A `tiles/` pipeline (geofabrik pbf → tilemaker → `israel.mbtiles`, gitignored) plus a tiny committed `fixture.mbtiles` for tests. A Rust module (`tiles.rs`) reads MBTiles via rusqlite and serves `/tiles.json`, `/tiles/{z}/{x}/{y}.pbf` (TMS→XYZ Y-flip, gzip MVT), and `/glyphs/{fontstack}/{range}.pbf` via an axum HTTP server on port 9002, started alongside the existing WebSocket server. The React `MapWidget` swaps Leaflet for MapLibre GL, points a dark vector style at the local endpoints, and draws the track as a GeoJSON layer.

**Tech Stack:** Rust (rusqlite, axum, tokio — tokio already present), Python 3.11 stdlib (fixture builder), React 19 + TS, `maplibre-gl`, vitest, Playwright, tilemaker (external, documented).

## Global Constraints

- TS strict, **no `any`**. React 19 `import type React`. Theme CSS vars only (no hardcoded hex in component CSS).
- Rust: no `unwrap()` on fallible IO in the server path — return errors/empty responses. Keep the existing WS server behavior unchanged.
- Reuse the data layer (`store.gpsTrack()`); MapWidget consumes it.
- MapLibre, like Leaflet, is GUI/WebGL — its map-init **no-ops in jsdom** (zero-size / no-WebGL guard); the vector map is build+live-verified, pure helpers carry unit coverage.
- MBTiles is **TMS** (origin bottom-left); MapLibre requests **XYZ** (top-left): `row = (1 << z) - 1 - y`. tilemaker stores **gzipped MVT** → serve `Content-Encoding: gzip`, `Content-Type: application/x-protobuf`.
- `israel.mbtiles` and downloaded glyph sets are **gitignored** (generated). `tiles/fixture.mbtiles` is **committed** (tiny).
- Ports: WS `9001` (`RIDE_WS_PORT`), tiles HTTP `9002` (`RIDE_TILES_PORT`). Tile/glyph paths via `RIDE_MBTILES` / `RIDE_GLYPHS`.

## File Structure

- `tiles/make_fixture.py` (new) — builds the committed `tiles/fixture.mbtiles`.
- `tiles/fixture.mbtiles` (new, committed) — tiny test MBTiles.
- `tiles/README.md` (new) — geofabrik + tilemaker + glyph build docs.
- `.gitignore` (modify) — ignore `tiles/israel.mbtiles`, `tiles/*.osm.pbf`, `tiles/glyphs/`.
- `rust/src-tauri/src/tiles.rs` (new) — MBTiles reader + axum server.
- `rust/src-tauri/src/lib.rs` (modify) — start the tile server in setup; declare `mod tiles`.
- `rust/src-tauri/Cargo.toml` (modify) — add `axum`.
- `rust/src/ui/app/widgets/trackGeo.ts` (new) — `trackToGeoJSON` pure helper.
- `rust/src/ui/app/widgets/mapStyle.ts` (new) — dark vector style → local endpoints.
- `rust/src/ui/app/widgets/MapWidget.tsx` (modify) — MapLibre swap.
- `rust/package.json` (modify) — drop `leaflet`/`@types/leaflet`, add `maplibre-gl`.
- Delete: `rust/src/ui/{Dashboard,GpsMap,Gauge,Hud,ParamTable,StripChart}.tsx` (+ their `.test.tsx`) — dead since the rewrite.
- Tests alongside each new module; `rust/src-tauri/tests/tiles_integration.rs` (new).

---

### Task 1: Test fixture MBTiles + pipeline docs

**Files:**
- Create: `tiles/make_fixture.py`, `tiles/README.md`
- Create (generated, committed): `tiles/fixture.mbtiles`
- Modify: `.gitignore`
- Test: `tiles/test_make_fixture.py`

**Interfaces:** `make_fixture.py` writes a valid MBTiles SQLite to a path (default `tiles/fixture.mbtiles`): tables `metadata(name TEXT, value TEXT)` and `tiles(zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB)`, a few gzipped dummy tiles, and metadata rows (`name, format=pbf, minzoom, maxzoom, bounds, center, json` with `vector_layers`). The Rust server does not parse MVT, so dummy gzipped bytes are sufficient for server tests.

- [ ] **Step 1: Write the failing test**

```python
# tiles/test_make_fixture.py
import gzip, sqlite3, os, subprocess, sys

def test_fixture_built(tmp_path):
    out = tmp_path / "fixture.mbtiles"
    subprocess.run([sys.executable, os.path.join(os.path.dirname(__file__), "make_fixture.py"), "--out", str(out)], check=True)
    con = sqlite3.connect(out)
    # schema
    names = {r[0] for r in con.execute("SELECT name FROM sqlite_master WHERE type='table'")}
    assert {"tiles", "metadata"} <= names
    # at least one tile, gzipped
    z, x, y, blob = con.execute("SELECT zoom_level,tile_column,tile_row,tile_data FROM tiles LIMIT 1").fetchone()
    assert gzip.decompress(blob)  # decompresses
    # metadata format=pbf + zoom bounds present
    meta = dict(con.execute("SELECT name,value FROM metadata"))
    assert meta["format"] == "pbf"
    assert "minzoom" in meta and "maxzoom" in meta and "bounds" in meta
    con.close()
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd tiles && python -m pytest test_make_fixture.py -v`
Expected: FAIL (make_fixture.py missing).

- [ ] **Step 3: Implement `make_fixture.py`**

```python
#!/usr/bin/env python3
"""Build a tiny committed MBTiles fixture for Rust tile-server tests.
Dummy gzipped MVT payloads — the server serves blobs verbatim, it does not parse MVT."""
import argparse, gzip, json, sqlite3

def build(out: str) -> None:
    con = sqlite3.connect(out)
    con.executescript(
        "DROP TABLE IF EXISTS tiles; DROP TABLE IF EXISTS metadata;"
        "CREATE TABLE metadata (name TEXT, value TEXT);"
        "CREATE TABLE tiles (zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB);"
        "CREATE UNIQUE INDEX tile_index ON tiles (zoom_level, tile_column, tile_row);"
    )
    # A few tiles across 2 zooms. tile_row is TMS (bottom-left origin).
    payloads = [(0, 0, 0), (1, 0, 0), (1, 1, 1)]
    for z, x, y in payloads:
        blob = gzip.compress(f"fixture-tile-{z}-{x}-{y}".encode())
        con.execute("INSERT INTO tiles VALUES (?,?,?,?)", (z, x, y, blob))
    meta = {
        "name": "fixture", "format": "pbf", "minzoom": "0", "maxzoom": "1",
        "bounds": "34.2,29.4,35.9,33.4", "center": "34.8,31.5,1",
        "json": json.dumps({"vector_layers": [{"id": "water"}, {"id": "transportation"}, {"id": "place"}]}),
    }
    for k, v in meta.items():
        con.execute("INSERT INTO metadata VALUES (?,?)", (k, v))
    con.commit()
    con.close()

if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="fixture.mbtiles")
    build(ap.parse_args().out)
```

- [ ] **Step 4: Generate the committed fixture + run test**

Run:
```
cd tiles && python make_fixture.py --out fixture.mbtiles && python -m pytest test_make_fixture.py -v
```
Expected: PASS; `tiles/fixture.mbtiles` exists.

- [ ] **Step 5: `.gitignore` + README**

Append to `.gitignore`:
```
tiles/israel.mbtiles
tiles/*.osm.pbf
tiles/glyphs/
```

`tiles/README.md` (the real-build doc):
```markdown
# Offline Israel vector tiles

## Build israel.mbtiles (~1–3 GB, gitignored)
wget https://download.geofabrik.de/asia/israel-and-palestine-latest.osm.pbf
tilemaker --input israel-and-palestine-latest.osm.pbf \
  --output israel.mbtiles --config config.json --process process.lua
# config.json / process.lua: tilemaker's OpenMapTiles-compatible resources.

## Glyphs (offline labels, gitignored)
# Noto Sans Regular/Bold glyph PBFs from the openmaptiles/fonts build → tiles/glyphs/<fontstack>/<range>.pbf

## Wire the app
RIDE_MBTILES=tiles/israel.mbtiles RIDE_GLYPHS=tiles/glyphs RIDE_TILES_PORT=9002

## Test fixture
python make_fixture.py   # writes tiles/fixture.mbtiles (committed; used by cargo tests)
```

- [ ] **Step 6: Commit**

```bash
git add tiles/make_fixture.py tiles/test_make_fixture.py tiles/fixture.mbtiles tiles/README.md .gitignore
git commit -m "feat(tiles): committed MBTiles test fixture + pipeline docs"
```

---

### Task 2: Rust MBTiles reader

**Files:**
- Create: `rust/src-tauri/src/tiles.rs` (reader portion)
- Modify: `rust/src-tauri/src/lib.rs` (add `mod tiles;`)
- Test: in `tiles.rs` `#[cfg(test)]` against `../../tiles/fixture.mbtiles`

**Interfaces:**
- `pub struct MbTiles { conn: rusqlite::Connection }`
- `MbTiles::open(path: &str) -> anyhow::Result<MbTiles>`
- `fn tile_xyz(&self, z: u32, x: u32, y: u32) -> rusqlite::Result<Option<Vec<u8>>>` — Y-flip XYZ→TMS internally (`row = (1<<z)-1-y`).
- `fn metadata(&self) -> rusqlite::Result<std::collections::HashMap<String,String>>`
- `fn tilejson(&self, tiles_url: &str) -> serde_json::Value` — builds TileJSON from metadata (tiles=[tiles_url], minzoom/maxzoom/bounds/center + vector_layers parsed from the `json` metadata key).

- [ ] **Step 1: Write the failing test** (in `tiles.rs`)

```rust
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
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd rust/src-tauri && cargo test tiles::tests`
Expected: FAIL (module not found).

- [ ] **Step 3: Implement the reader** (top of `tiles.rs`)

```rust
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
        let bounds: Vec<f64> = meta.get("bounds").map(|b| b.split(',').filter_map(|s| s.parse().ok()).collect()).unwrap_or_else(|| vec![-180.0, -85.0, 180.0, 85.0]);
        let vector_layers = meta.get("json")
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
```

Add `mod tiles;` to `lib.rs`. (`anyhow` + `serde_json` are already dependencies.)

- [ ] **Step 4: Run to verify it passes**

Run: `cd rust/src-tauri && cargo test tiles::tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add rust/src-tauri/src/tiles.rs rust/src-tauri/src/lib.rs
git commit -m "feat(rust): MBTiles reader (Y-flip, metadata, TileJSON)"
```

---

### Task 3: axum tile + glyph HTTP server

**Files:**
- Modify: `rust/src-tauri/src/tiles.rs` (server portion)
- Modify: `rust/src-tauri/Cargo.toml` (add `axum = "0.7"`)
- Test: `rust/src-tauri/tests/tiles_integration.rs`

**Interfaces:**
- `pub async fn serve(addr: std::net::SocketAddr, mbtiles: Option<String>, glyphs: Option<String>) -> anyhow::Result<()>` — binds axum with routes:
  - `GET /tiles.json` → TileJSON (200 + json), or `{}` if no mbtiles.
  - `GET /tiles/:z/:x/:y` (where `:y` is `"{n}.pbf"`) → tile bytes (200, `Content-Type: application/x-protobuf`, `Content-Encoding: gzip`) or `204` when absent / no mbtiles.
  - `GET /glyphs/:fontstack/:range` → glyph PBF bytes from `glyphs` dir or `404`.
- The `MbTiles` connection is wrapped for shared async access (rusqlite `Connection` is not `Sync`; open it inside a `tokio::task::spawn_blocking` per request, or guard with a `std::sync::Mutex` in the axum state — use a `Mutex<MbTiles>` in `axum::extract::State` since tile reads are fast).

- [ ] **Step 1: Write the failing integration test**

```rust
// rust/src-tauri/tests/tiles_integration.rs
use std::net::SocketAddr;

#[tokio::test]
async fn serves_tiles_tilejson_and_204() {
    let fix = concat!(env!("CARGO_MANIFEST_DIR"), "/../../tiles/fixture.mbtiles").to_string();
    let addr: SocketAddr = "127.0.0.1:0".parse().unwrap();
    let listener = tokio::net::TcpListener::bind(addr).await.unwrap();
    let bound = listener.local_addr().unwrap();
    tokio::spawn(async move { app_lib::tiles::serve_with_listener(listener, Some(fix), None).await.unwrap(); });
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
```

(Add `reqwest = { version = "0.12", features = ["blocking"] , default-features = false, features=["http2"] }` — simplest: `reqwest = "0.12"` — as a `[dev-dependencies]`. Expose `serve_with_listener` so the test can bind port 0.)

- [ ] **Step 2: Run to verify it fails**

Run: `cd rust/src-tauri && cargo test --test tiles_integration`
Expected: FAIL (server not implemented).

- [ ] **Step 3: Implement the server** (append to `tiles.rs`)

```rust
use axum::{extract::{Path, State}, http::{header, StatusCode}, response::IntoResponse, routing::get, Router};
use std::sync::{Arc, Mutex};

#[derive(Clone)]
struct AppState {
    mbtiles: Option<Arc<Mutex<MbTiles>>>,
    glyphs: Option<String>,
}

pub async fn serve(addr: std::net::SocketAddr, mbtiles: Option<String>, glyphs: Option<String>) -> anyhow::Result<()> {
    let listener = tokio::net::TcpListener::bind(addr).await?;
    serve_with_listener(listener, mbtiles, glyphs).await
}

pub async fn serve_with_listener(listener: tokio::net::TcpListener, mbtiles: Option<String>, glyphs: Option<String>) -> anyhow::Result<()> {
    let mb = match mbtiles {
        Some(p) => MbTiles::open(&p).ok().map(|m| Arc::new(Mutex::new(m))),
        None => None,
    };
    let state = AppState { mbtiles: mb, glyphs };
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
            let tj = m.lock().unwrap().tilejson("http://127.0.0.1:9002/tiles/{z}/{x}/{y}.pbf");
            axum::Json(tj).into_response()
        }
        None => axum::Json(serde_json::json!({})).into_response(),
    }
}

async fn tile_handler(State(s): State<AppState>, Path((z, x, y)): Path<(u32, u32, String)>) -> impl IntoResponse {
    let y = y.trim_end_matches(".pbf").parse::<u32>().unwrap_or(u32::MAX);
    let blob = s.mbtiles.as_ref().and_then(|m| m.lock().unwrap().tile_xyz(z, x, y).ok().flatten());
    match blob {
        Some(bytes) => (
            StatusCode::OK,
            [(header::CONTENT_TYPE, "application/x-protobuf"), (header::CONTENT_ENCODING, "gzip")],
            bytes,
        ).into_response(),
        None => StatusCode::NO_CONTENT.into_response(),
    }
}

async fn glyph_handler(State(s): State<AppState>, Path((fontstack, range)): Path<(String, String)>) -> impl IntoResponse {
    let dir = match &s.glyphs { Some(d) => d, None => return StatusCode::NOT_FOUND.into_response() };
    let path = std::path::Path::new(dir).join(&fontstack).join(&range);
    match std::fs::read(path) {
        Ok(bytes) => (StatusCode::OK, [(header::CONTENT_TYPE, "application/x-protobuf")], bytes).into_response(),
        Err(_) => StatusCode::NOT_FOUND.into_response(),
    }
}
```

Add `axum = "0.7"` to `[dependencies]` and `reqwest = "0.12"` to `[dev-dependencies]` in `Cargo.toml`.

- [ ] **Step 4: Run to verify it passes**

Run: `cd rust/src-tauri && cargo test --test tiles_integration && cargo test`
Expected: PASS (integration + existing ws test).

- [ ] **Step 5: Commit**

```bash
git add rust/src-tauri/src/tiles.rs rust/src-tauri/Cargo.toml rust/src-tauri/tests/tiles_integration.rs
git commit -m "feat(rust): axum tile + glyph HTTP server over MBTiles"
```

---

### Task 4: Start the tile server in the Tauri app

**Files:**
- Modify: `rust/src-tauri/src/lib.rs` (setup hook)

**Interfaces:** In the Tauri `setup` (where the WS server is spawned), also spawn `tiles::serve` on `127.0.0.1:<RIDE_TILES_PORT default 9002>`, reading `RIDE_MBTILES` (default: auto-resolve `tiles/israel.mbtiles`, else `tiles/fixture.mbtiles`) and `RIDE_GLYPHS` (default `tiles/glyphs`). Failures log but do not crash the app.

- [ ] **Step 1: Implement** — in `lib.rs` setup, alongside the existing WS spawn:

```rust
let tiles_port: u16 = std::env::var("RIDE_TILES_PORT").ok().and_then(|v| v.parse().ok()).unwrap_or(9002);
let mbtiles = std::env::var("RIDE_MBTILES").ok().or_else(|| resolve_default_mbtiles());
let glyphs = std::env::var("RIDE_GLYPHS").ok().or_else(|| Some("tiles/glyphs".to_string()));
tauri::async_runtime::spawn(async move {
    let addr = std::net::SocketAddr::from(([127, 0, 0, 1], tiles_port));
    if let Err(e) = crate::tiles::serve(addr, mbtiles, glyphs).await {
        eprintln!("tile server error: {e}");
    }
});
```

Add a small `resolve_default_mbtiles()` mirroring however the app already auto-resolves `ride.db` (reuse that helper's directory-walk if present; otherwise check `tiles/israel.mbtiles` then `tiles/fixture.mbtiles` relative to the workspace).

- [ ] **Step 2: Build-verify**

Run: `cd rust/src-tauri && cargo build && cargo test`
Expected: builds; tests pass. (No new unit test — this is wiring; covered by Task 3 integration + live verify.)

- [ ] **Step 3: Commit**

```bash
git add rust/src-tauri/src/lib.rs
git commit -m "feat(rust): start tile server alongside WS in Tauri setup"
```

---

### Task 5: `trackToGeoJSON` helper

**Files:**
- Create: `rust/src/ui/app/widgets/trackGeo.ts`
- Test: `rust/src/ui/app/widgets/trackGeo.test.ts`

**Interfaces:** `trackToGeoJSON(lat: number[], lon: number[]): GeoJSON.Feature<GeoJSON.LineString>` — a `LineString` of `[lon, lat]` pairs (GeoJSON is lon-first); empty/short input → an empty-coords LineString. (Use `geojson` types if present, else a local minimal type — no `any`.)

- [ ] **Step 1: Write the failing test**

```ts
import { describe, it, expect } from "vitest";
import { trackToGeoJSON } from "./trackGeo";

describe("trackToGeoJSON", () => {
  it("builds a lon,lat LineString", () => {
    const f = trackToGeoJSON([32.0, 32.1], [34.8, 34.9]);
    expect(f.geometry.type).toBe("LineString");
    expect(f.geometry.coordinates).toEqual([[34.8, 32.0], [34.9, 32.1]]);
  });
  it("empty input → empty coords", () => {
    expect(trackToGeoJSON([], []).geometry.coordinates).toEqual([]);
  });
});
```

- [ ] **Step 2–4:** run (FAIL) → implement → run (PASS).

```ts
export interface LineStringFeature {
  type: "Feature";
  properties: Record<string, never>;
  geometry: { type: "LineString"; coordinates: [number, number][] };
}

export function trackToGeoJSON(lat: number[], lon: number[]): LineStringFeature {
  const n = Math.min(lat.length, lon.length);
  const coordinates: [number, number][] = [];
  for (let i = 0; i < n; i++) coordinates.push([lon[i], lat[i]]);
  return { type: "Feature", properties: {}, geometry: { type: "LineString", coordinates } };
}
```

Run: `cd rust && npx vitest run src/ui/app/widgets/trackGeo.test.ts && npx tsc --noEmit`

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/widgets/trackGeo.ts rust/src/ui/app/widgets/trackGeo.test.ts
git commit -m "feat(rust-ui): trackToGeoJSON helper"
```

---

### Task 6: Dark vector map style → local endpoints

**Files:**
- Create: `rust/src/ui/app/widgets/mapStyle.ts`
- Test: `rust/src/ui/app/widgets/mapStyle.test.ts`

**Interfaces:** `export function mapStyle(tilesBase: string): StyleSpecification` — returns a MapLibre style object (typed from `maplibre-gl`'s `StyleSpecification`) adapted from a dark-matter base: `sources.basemap = { type:"vector", url:`${tilesBase}/tiles.json` }`, `glyphs:`${tilesBase}/glyphs/{fontstack}/{range}.pbf``, a dark background + water/land/road/place layers tuned toward the INU palette. Keep the layer set modest (water, landcover, transportation casing+line, building, place labels) referencing OpenMapTiles source-layers.

- [ ] **Step 1: Write the failing test**

```ts
import { describe, it, expect } from "vitest";
import { mapStyle } from "./mapStyle";

describe("mapStyle", () => {
  it("points sources + glyphs at the local base and has layers", () => {
    const s = mapStyle("http://127.0.0.1:9002");
    expect(s.sources.basemap).toMatchObject({ type: "vector", url: "http://127.0.0.1:9002/tiles.json" });
    expect(s.glyphs).toBe("http://127.0.0.1:9002/glyphs/{fontstack}/{range}.pbf");
    expect(Array.isArray(s.layers) && s.layers.length).toBeGreaterThan(0);
    // a background layer in the INU bg
    const bg = s.layers.find((l) => l.type === "background");
    expect(bg).toBeTruthy();
  });
});
```

- [ ] **Step 2–4:** run (FAIL) → implement `mapStyle.ts` (a compact dark style; import `StyleSpecification` from `maplibre-gl` — added in Task 7's dep change, so this task may need `maplibre-gl` installed first; if executing in order, add the dep in this task's Step 3 via `npm i maplibre-gl` and `npm i -D @types` not needed since maplibre ships types) → run (PASS) + `npx tsc --noEmit`.

> Implementer note: keep the style literal small but valid; values verbatim from a dark-matter cut, with `background` paint `#0a0e14`, water `#0d1a24`, roads in muted greys, place labels using `"text-font": ["Noto Sans Regular"]` (matching the bundled glyph fontstack).

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/widgets/mapStyle.ts rust/src/ui/app/widgets/mapStyle.test.ts rust/package.json rust/package-lock.json
git commit -m "feat(rust-ui): dark MapLibre vector style → local endpoints"
```

---

### Task 7: MapWidget — swap Leaflet for MapLibre GL

**Files:**
- Modify: `rust/src/ui/app/widgets/MapWidget.tsx`
- Modify: `rust/package.json` (remove `leaflet`/`@types/leaflet` if not already; ensure `maplibre-gl`)
- Test: `rust/src/ui/app/widgets/MapWidget.test.tsx`

**Interfaces:** `MapWidget` keeps the SVG mode + chrome + toggle. When the map is on, mount a MapLibre `Map` (`style: mapStyle("http://127.0.0.1:<port>")`) into the overlay div, add a `track` GeoJSON source + line layer (`--accent`) + a marker at the last point, and `source.setData(trackToGeoJSON(lat, lon))` on track change. **jsdom guard:** bail before `new maplibregl.Map(...)` when the container has zero size (jsdom) — so unit tests never construct a GL map. The tiles port is read from `import.meta.env` or a constant (9002). Remove all Leaflet imports/usage.

- [ ] **Step 1: Write the failing test (extend MapWidget.test.tsx)** — assert toggling shows/removes the `.mapwidget-osm` overlay (unchanged behavior), and that no MapLibre GL is constructed under jsdom (no throw). Keep the chrome tests.

- [ ] **Step 2–4:** run (FAIL) → implement (mirror the Phase-5 Leaflet lifecycle shape: `[osm]` mount effect guarded on zero-size; `[lat,lon,osm]` update effect calling `source.setData`; cleanup `map.remove()`). `import maplibregl from "maplibre-gl"; import "maplibre-gl/dist/maplibre-gl.css";`. → run (PASS); full `npm test`; `npx tsc --noEmit`; `npm run build`.

> Implementer note: MapLibre marker = `new maplibregl.Marker()` or a `circle` layer on a single-point GeoJSON; choose the layer approach to avoid DOM-marker lifecycle. Guard all GL calls behind the zero-size check.

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/widgets/MapWidget.tsx rust/src/ui/app/widgets/MapWidget.test.tsx rust/package.json rust/package-lock.json
git commit -m "feat(rust-ui): MapWidget renders MapLibre GL vector basemap + track"
```

---

### Task 8: Remove the dead pre-rewrite UI tree

**Files:**
- Delete: `rust/src/ui/Dashboard.tsx`, `GpsMap.tsx`, `Gauge.tsx`, `Gauge.test.tsx`, `Hud.tsx`, `Hud.test.tsx`, `ParamTable.tsx`, `ParamTable.test.tsx`, `StripChart.tsx` (keep `useTelemetry.ts` + `useTelemetry.test.tsx` — those are live).

**Interfaces:** none — pure deletion of code orphaned since the rewrite (`App.tsx` uses `ui/app/AppShell`; nothing imports these).

- [ ] **Step 1: Confirm no importers**

Run: `cd rust && grep -rnE "Dashboard|GpsMap|/Gauge\"|/Hud\"|ParamTable|StripChart" src --include=*.tsx --include=*.ts | grep -v "ui/app/" | grep -vE "\.test\.|//"`
Expected: only self-references among the files being deleted (and `leaflet` import only in `GpsMap.tsx`). If anything in `ui/app/**` imports them, STOP.

- [ ] **Step 2: Delete + verify**

```bash
git rm rust/src/ui/Dashboard.tsx rust/src/ui/GpsMap.tsx rust/src/ui/Gauge.tsx rust/src/ui/Gauge.test.tsx rust/src/ui/Hud.tsx rust/src/ui/Hud.test.tsx rust/src/ui/ParamTable.tsx rust/src/ui/ParamTable.test.tsx rust/src/ui/StripChart.tsx
cd rust && npx tsc --noEmit && npm test && npm run build
```
Expected: clean (no dangling imports), tests pass, build OK. (`leaflet` now has zero importers.)

- [ ] **Step 3: Drop the leaflet dependency**

Remove `leaflet` + `@types/leaflet` from `rust/package.json`; `npm install` to update the lock. Verify `npm run build` still green.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "chore(rust-ui): remove dead pre-rewrite UI tree + leaflet dep"
```

---

### Task 9: Playwright — map still SVG-baselined; toggle present

**Files:**
- Modify: `rust/e2e/map.spec.ts` (adjust toggle label if changed)

**Interfaces:** The vector map needs the Rust tile server (absent in mock-WS e2e), so do NOT screenshot the map ON. Keep the SVG-mode baseline + assert the toggle button is present. If Task 7 renamed the toggle label (`OSM MAP`→`MAP`), update the assertion. Adjust any chrome assertions if the overlay markup changed.

- [ ] **Step 1: Update the spec** so `map.spec.ts` asserts the toggle (by its current label/testid) + the SVG chrome, and screenshots GRID mode only.

- [ ] **Step 2: Clean regen** (avoid the stale-cache trap — this project has been bitten):
```bash
cd rust
# stop :1420; DELETE the snapshots you expect to change, then update:
rm -rf node_modules/.vite
rm -f e2e/map.spec.ts-snapshots/*.png   # and any others whose markup changed
npm run e2e:update
npm run e2e   # green
```

- [ ] **Step 3: Verify** — READ the regenerated `map-*.png` (and widgets/interactions if the map markup changed); confirm the SVG map + chrome render (not blank/stale). If blank → STOP.

- [ ] **Step 4: Commit**

```bash
git add rust/e2e/map.spec.ts rust/e2e/*-snapshots
git commit -m "test(rust-ui): map e2e for MapLibre toggle; regen affected baselines"
```

---

### Task 10: Full gate + live verify

- [ ] **Step 1: Full suite**

```bash
cd rust && npx tsc --noEmit && npm test && npm run build && npm run e2e
cd src-tauri && cargo test
```
Expected: all green (incl. the new tiles unit + integration tests).

- [ ] **Step 2: Live verify** (GUI; needs the real `israel.mbtiles` + glyphs)

Build the tiles per `tiles/README.md` (geofabrik + tilemaker), put glyphs in `tiles/glyphs/`, then:
```bash
cd rust && RIDE_DB=../../data/ride_small.db RIDE_MBTILES=../../tiles/israel.mbtiles RIDE_GLYPHS=../../tiles/glyphs RIDE_SPEED=2 npm run tauri dev
```
Confirm: toggling the map shows the **offline** Israel vector basemap (dark theme, labeled), the GPS **track polyline + marker** draw on it, pan/zoom works, no network. Toggle back to SVG. Resize the map cell.

- [ ] **Step 3: Commit any tweaks**, then finish the branch (PR).

---

## Self-Review

**Spec coverage:** pipeline + fixture (T1), MBTiles reader (T2), HTTP tile/glyph server (T3), Tauri wiring (T4), track GeoJSON (T5), dark style→local endpoints (T6), MapLibre swap + track-on-map (T7), dead-tree + leaflet removal (T8), e2e (T9), gate+live (T10). Glyphs served + style references them ✓; tiny committed fixture drives Rust tests ✓; Y-flip in reader + tested ✓. ✓

**Placeholder scan:** No TBD. The style (T6) and MapLibre lifecycle (T7) carry implementer notes rather than full literals because the dark-matter style JSON and the GL init are large/visual — the testable contracts (sources/glyphs keys; toggle + jsdom guard; `setData` wiring) are specified; visual correctness is live-verified per repo convention.

**Type/contract consistency:** `MbTiles` (T2) consumed by the server (T3) + wiring (T4); `tilejson` URL shape matches the style's `tiles.json` source (T6); `trackToGeoJSON` (T5) consumed by MapWidget (T7); tiles port 9002 consistent across T3/T4/T6/T7; TMS→XYZ Y-flip defined once in `tile_xyz` and asserted in T2 + T3. ✓
