# Tileset Auto-Provision on Startup — Design Spec

**Status:** design (Phase 7 of the INU-MONITOR Rust UI work)

## Goal

On app startup, ensure an offline Israel vector tileset exists. If `tiles/israel.mbtiles` is absent, **download** a prebuilt MBTiles from a configured URL; if that's not configured/possible, **convert** one from the geofabrik OSM extract via tilemaker. Provisioning is **blocking on first run** (console progress), then the app continues with the tile server. Labels are out of scope for now (no glyph provisioning).

## Provisioning chain (blocking, in the Tauri setup, before the tile server starts)

```
1. RIDE_MBTILES set (explicit path) and exists           → use it.
2. tiles/israel.mbtiles exists                            → use it.
3. RIDE_MBTILES_URL set                                   → download → tiles/israel.mbtiles → use it.
4. tilemaker on PATH + config/process available           → download geofabrik pbf → tilemaker convert → use it.
5. none of the above                                      → log instructions; start with NO mbtiles
                                                            (tile server returns 204; SVG map only).
```

Each step that produces a file falls through to the next on failure (download error, tilemaker missing/non-zero) — provisioning never crashes the app; worst case is step 5.

## Components

### 1. Rust `provision` module — `rust/src-tauri/src/provision.rs`

- `pub async fn ensure_mbtiles(cfg: ProvisionCfg) -> Option<String>` — runs the chain above, returns the resolved mbtiles path or `None`.
- `ProvisionCfg { mbtiles_path: String, mbtiles_url: Option<String>, pbf_url: Option<String>, tilemaker_config: Option<String>, tilemaker_process: Option<String> }` — built from env.
- **Download helper** `async fn download_to(url: &str, dest: &str) -> anyhow::Result<()>` — streams the response body to a temp file with periodic `eprintln!` progress (bytes / %), then atomically renames to `dest`. Uses `reqwest` (moved to `[dependencies]` with TLS, since runtime HTTPS is now needed; rustls to avoid an OpenSSL build dep).
- **Convert helper** `fn run_tilemaker(pbf: &str, out: &str, config: &str, process: &str) -> anyhow::Result<()>` — `std::process::Command::new("tilemaker")` with the args; returns Err if the binary is absent (spawn error) or exits non-zero. The tilemaker `config.json`/`process.lua` come from env paths (`RIDE_TILEMAKER_CONFIG`/`PROCESS`), defaulting to `tiles/config.json`/`tiles/process.lua` if present, else the convert step is skipped (Err) — the README points users at tilemaker's bundled `resources/config-openmaptiles.json` + `process-openmaptiles.lua`.
- The **decision chain** is unit-testable by injecting the side-effecting steps: `ensure_mbtiles_with(exists_fn, download_fn, convert_fn, cfg)` takes closures so a test can assert the order (local→url→tilemaker→none) without real IO; the public `ensure_mbtiles` wires the real closures.

### 2. Tauri wiring — `rust/src-tauri/src/lib.rs`

In `setup`, **before** spawning the tile server, `tauri::async_runtime::block_on(provision::ensure_mbtiles(cfg))` (blocking first-run) to get the resolved path, then start the tile server with it. Env read here: `RIDE_MBTILES`, `RIDE_MBTILES_URL`, `RIDE_PBF_URL` (default `https://download.geofabrik.de/asia/israel-and-palestine-latest.osm.pbf`), `RIDE_TILEMAKER_CONFIG`, `RIDE_TILEMAKER_PROCESS`. Glyphs unchanged (still `RIDE_GLYPHS`, optional).

### 3. Style — `rust/src/ui/app/widgets/mapStyle.ts`

Since labels are skipped, **remove the `place-label` layer and the `glyphs` URL** from the style so MapLibre doesn't emit glyph-fetch 404s. The basemap renders geometry (water/landcover/landuse/transportation/building) only. (Re-add glyphs + the label layer in a later labels phase.) Update `mapStyle.test.ts` accordingly (no `glyphs` assertion; assert the label layer is absent / the geometry layers present).

### 4. Docs — `tiles/README.md`

Document the provisioning chain + env: set `RIDE_MBTILES_URL` to a prebuilt Israel `.mbtiles` to auto-download; or install tilemaker + point `RIDE_TILEMAKER_CONFIG`/`PROCESS` at its OpenMapTiles resources for the convert fallback; or drop a prebuilt `tiles/israel.mbtiles` in manually.

## Data flow

App start → setup reads env → `ensure_mbtiles` (blocking): file present? use it. Else URL set? stream-download (progress to console). Else tilemaker? download pbf + convert. Else none. → tile server starts on the resolved path (or serves 204 if none) → MapWidget OSM toggle renders the offline basemap (geometry, no labels).

## Testing

- **Rust** (`cargo test`): unit-test `ensure_mbtiles_with` with injected closures — (a) local file exists → returns it without download/convert; (b) no file + url set → calls download → returns path; (c) no file + no url + tilemaker "available" → calls convert; (d) nothing → `None`. Assert call order + that earlier wins. The real `download_to`/`run_tilemaker` are live/manual (network + external binary), not CI-tested.
- **Frontend** (vitest): `mapStyle.test.ts` updated for the label/glyphs removal.
- **Live verify:** set `RIDE_MBTILES_URL` to a real prebuilt Israel mbtiles (or pre-place `tiles/israel.mbtiles`), launch, toggle the map → offline geometry basemap + GPS track.

## Conventions / decisions (locked)

- Primary = **download a prebuilt mbtiles from a config URL** (`RIDE_MBTILES_URL`); **no hardcoded default URL** (none is reliably free/stable). Fallback = **tilemaker convert**. **Blocking first-run** with console progress. **Labels skipped** (no glyph provisioning; style drops the label layer). `israel.mbtiles` stays gitignored; the committed `fixture.mbtiles` (dummy tiles) is for Rust tests only — it is NOT used as a runtime fallback for the real map (a fixture-only launch shows a blank basemap, which is expected).

## Risks / notes

- A prebuilt Israel **vector** mbtiles is not trivially available for free without auth; the user supplies the URL. The tilemaker fallback is self-contained but needs the C++ tool + its OMT resources.
- Blocking startup on a multi-GB download/convert means the window appears only after provisioning on first run — accepted per the chosen UX. Subsequent runs find the file and start immediately.
- `reqwest` moves to a runtime dependency (rustls TLS) — a modest bundle/compile cost.

## Out of scope

- Place labels / glyph provisioning (separate later phase).
- Progress UI in React (console logging only this phase).
- Multiple regions (Israel only; the URL/pbf env make swapping trivial).
