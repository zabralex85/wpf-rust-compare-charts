# Offline MapLibre Vector Basemap — Design Spec

**Status:** design (Phase 6 of the INU-MONITOR Rust UI work)

## Goal

Replace the Phase-5 online-Leaflet OSM basemap with an **offline** MapLibre GL vector map of **Israel**. Vector tiles are built once with tilemaker into an MBTiles file; the Rust/Tauri backend serves tiles + glyphs over local HTTP; the React `MapWidget` renders them with MapLibre GL and draws the live GPS **track polyline + marker** on the map. No network at runtime.

## Architecture

```
Geofabrik israel-and-palestine-latest.osm.pbf
        │  tilemaker --config config.json --process process.lua
        ▼
israel.mbtiles  (~1–3 GB vector MVT, gitignored)        tiles/glyphs/*.pbf  (bundled font glyphs)
        │                                                        │
        ▼   rusqlite (read-only)                                 ▼
Rust/Tauri backend  ──  local HTTP server (port 9002)
   GET /tiles.json                → TileJSON (from MBTiles metadata)
   GET /tiles/{z}/{x}/{y}.pbf     → gzip MVT blob (TMS→XYZ Y-flip)
   GET /glyphs/{fontstack}/{range}.pbf → glyph PBF
        │
        ▼  fetch
MapLibre GL JS  (dark-matter-derived style → local endpoints)
   + GeoJSON track line layer + position marker
        ▼
React / Tauri  (MapWidget; Leaflet removed)
```

The tile HTTP server runs **alongside** the existing telemetry WebSocket server (`ws://127.0.0.1:9001`). The WS path (meta/frame/metrics) is unchanged.

## Components

### 1. Tile build pipeline — `tiles/` (repo root, dev/offline step)

- **Source:** `wget https://download.geofabrik.de/asia/israel-and-palestine-latest.osm.pbf`.
- **Build:** `tilemaker --input israel-and-palestine-latest.osm.pbf --output israel.mbtiles --config tiles/config.json --process tiles/process.lua`. Use tilemaker's standard **OpenMapTiles-compatible** config + Lua profile (shipped in `tiles/`), so the vector schema matches the dark-matter style's expected layers (`water`, `waterway`, `landuse`, `landcover`, `transportation`, `building`, `boundary`, `place`, `transportation_name`, …).
- **Output:** `israel.mbtiles` (~1–3 GB) — **gitignored** (like `ride.db`), generated locally.
- **Glyphs:** font glyph PBFs (e.g. **Noto Sans Regular / Bold** from the `openmaptiles/fonts` build) placed in `tiles/glyphs/<fontstack>/<range>.pbf`. A fetch script (`tiles/fetch-glyphs.sh`) downloads them; the small set actually referenced by the style may be committed, the rest gitignored.
- **Fixture:** `tiles/fixture.mbtiles` — a **tiny committed** MBTiles (small bbox over the ride area, ~2 zoom levels, a few KB) built once and checked in, mirroring the `ride_small.db` fixture pattern. Used by Rust tile-server tests.
- **Docs:** `tiles/README.md` with the wget + tilemaker + glyph commands and the env wiring.

### 2. Rust tile server — `rust/src-tauri/src/tiles.rs`

- Opens the MBTiles read-only via **rusqlite** (already a dependency). Standard MBTiles schema: `tiles(zoom_level, tile_column, tile_row, tile_data)` + `metadata(name, value)`.
- An **HTTP server** (add `axum` — sits naturally on the existing `tokio` runtime) bound to `127.0.0.1:<RIDE_TILES_PORT>` (default **9002**). Endpoints:
  - `GET /tiles.json` — TileJSON `{ tiles, minzoom, maxzoom, bounds, center, vector_layers }` derived from the `metadata` table.
  - `GET /tiles/{z}/{x}/{y}.pbf` — the MVT blob for that tile. MBTiles is **TMS** (origin bottom-left); MapLibre requests **XYZ** (top-left), so flip: `row = (1 << z) - 1 - y`. Respond `Content-Type: application/x-protobuf`, `Content-Encoding: gzip` (tilemaker stores gzipped MVT). Missing tile → `204 No Content`.
  - `GET /glyphs/{fontstack}/{range}.pbf` — read the glyph PBF from `RIDE_GLYPHS` dir; missing → `404`.
- **Env:** `RIDE_MBTILES` (path; defaults to an auto-resolved `tiles/israel.mbtiles`, falling back to `tiles/fixture.mbtiles`), `RIDE_GLYPHS` (glyphs dir), `RIDE_TILES_PORT` (9002).
- Started from the Tauri setup hook next to the WS server. If the MBTiles file is absent, the server still starts and returns `204` for tiles (the UI shows the SVG fallback).

### 3. MapLibre frontend — `MapWidget.tsx` (+ helpers)

- Remove `leaflet` + `@types/leaflet`; add **`maplibre-gl`** + its CSS.
- The existing `osm` toggle (kept; label may change to `MAP`↔`GRID`) mounts a MapLibre `Map` into the overlay div instead of Leaflet.
- **Style:** a committed **dark-matter-derived** style (`rust/src/ui/app/widgets/mapStyle.ts` exporting a `StyleSpecification`, or a `.json`), tuned to the INU palette (`--bg #0a0e14`, water/land/road tones, cyan accents), with:
  - `sources.basemap = { type: "vector", url: "http://127.0.0.1:9002/tiles.json" }`
  - `glyphs: "http://127.0.0.1:9002/glyphs/{fontstack}/{range}.pbf"`
  - layers referencing the OpenMapTiles source layers.
- **Track:** a GeoJSON source (`track`) + a line layer (cyan `--accent`) and a marker (a `circle`/`symbol` layer or an `HTMLMarker`) at the last point. A pure helper `trackToGeoJSON(lat, lon)` (unit-tested) builds the `LineString`; the component calls `source.setData(...)` as the track grows.
- **jsdom/test guard:** MapLibre needs WebGL + real layout → like Leaflet, the map-init effect **no-ops in jsdom** (guard on zero-size container and/or absent WebGL). The toggle's DOM behavior + `trackToGeoJSON` are unit-tested; the live map is build/live-verified.

### 4. Data flow

App start → Rust opens MBTiles + starts the tile HTTP server (9002) and the WS server (9001). User toggles the map on → MapLibre loads the style → fetches `tiles.json` / tiles / glyphs from Rust → renders the offline Israel vector map. Telemetry frames → `store.gpsTrack()` → `MapWidget` updates the `track` GeoJSON source → line + marker move (pan only when the marker leaves view, as in Phase 5).

## Testing

- **Rust** (`cargo test`): unit tests for the MBTiles reader against the committed `tiles/fixture.mbtiles` — tile fetch, **Y-flip** correctness, `metadata`→TileJSON; an HTTP integration test (mirroring the existing `ws_integration`) hitting `/tiles.json`, `/tiles/{z}/{x}/{y}.pbf` (200 + gzip header for a present tile, 204 for an absent one), and `/glyphs/...`.
- **Frontend** (vitest): `trackToGeoJSON` pure helper; `MapWidget` toggle + track-data wiring with MapLibre guarded off in jsdom.
- **Playwright:** MapLibre tiles need the Rust server, which the mock-WS e2e doesn't run — so the **vector map itself is live-verified**, not screenshotted in CI (consistent with the repo's chart/map convention). The SVG-mode baseline + toggle-present assertion remain.
- **Tile pipeline** (tilemaker, 1–3 GB) is a **documented manual build**, not CI-tested; CI builds the app, not the tiles.

## Conventions / decisions (locked)

- **Vector** tiles (not raster). **Bundled glyphs** served locally (offline labels — Noto Sans). **Tiny committed `fixture.mbtiles`** for tests; full `israel.mbtiles` gitignored. **Adapted community dark style** (dark-matter) repointed to local endpoints. **Rust HTTP proxy** on port 9002 via rusqlite + axum. **Leaflet removed.** Track polyline renders on the basemap.

## Risks / notes

- `maplibre-gl` (~800 KB) replaces `leaflet` (~150 KB) — larger bundle, but vector rendering + offline is the requirement.
- WebGL is available in the Tauri WebView2 (Win10/11) — confirm on live verify.
- tilemaker's OpenMapTiles config/Lua must produce the source layers the dark-matter style references; mismatches show as missing features (tune the style or the config).
- The dead pre-rewrite `rust/src/ui/{Dashboard,GpsMap,Gauge,Hud,ParamTable,StripChart}.tsx` tree (orphaned since the rewrite) should be deleted in this phase, since Leaflet/`GpsMap` go away.

## Out of scope

- 3D terrain, routing, search/geocoding, multiple regions (Israel only, though the pipeline + `RIDE_MBTILES` make swapping regions trivial).
