# wpf-rust-compare-charts

Render the **same realtime telemetry dashboard in two stacks** and compare their performance.

An INU-style monitoring dashboard — live parameter table, scrolling strip charts, radial gauges, GPS map, and a perf HUD — is replayed from a shared SQLite "ride" and built twice:

| | Rust app | .NET app |
|---|---|---|
| Stack | Tauri 2 + React + TypeScript | native **WPF / XAML** (MVVM, C#) |
| Charts | uPlot (scrolling time-series) | ScottPlot.WPF |
| Map | MapLibre GL — **offline** vector basemap | native MVT/Skia renderer — **offline** MBTiles |
| Transport | local WebSocket | in-process |

This is a **paradigm contrast**: a web WebView UI (Tauri/React) vs a native retained-mode GPU UI (WPF) — each stack in its own idiom. Both read the **same `ride.db`** and show the same HUD — FPS, frame time, end-to-end latency, CPU% (per-core), RAM — so the two stacks can be compared head to head. Target layout: [`docs/reference/dashboard-target.md`](docs/reference/dashboard-target.md).

> The .NET app was originally WPF + Blazor Hybrid and has been reskinned to native WPF/XAML to match the Rust INU dashboard. Both apps are now fully reskinned (parameters, gauges, charts, offline map, transport, interactive widget grid, perf HUD).

## Screenshots

**.NET — native WPF / XAML** (offline MVT/Skia map)

![.NET WPF INU-MONITOR dashboard](docs/screenshots/dotnet-wpf.png)

**Rust — Tauri + React** (uPlot charts, MapLibre map)

![Rust INU-MONITOR dashboard](docs/screenshots/rust.png)

## Measured footprint

Both apps replaying the same ride at `RIDE_SPEED=1.0` with the full dashboard + offline map, **Release** builds. Sampled after a 30 s warm‑up over a 5 s window on an 8‑core Windows 11 machine. CPU is `% of total` system capacity (the Task Manager convention); RAM is working set. The Rust figure sums **app + WebView2** (7 processes) since that's the real footprint of a Tauri app.

| Stack | Processes | RAM | CPU (total) |
|---|---|---|---|
| **.NET WPF** (native retained‑mode) | 1 | ~246 MB | ~4% |
| **Rust Tauri** (React in WebView2) | 7 (app + WebView2 tree) | ~629 MB | ~11% |

The native WPF app is markedly lighter on both memory and CPU — the bundled Chromium **WebView2** runtime dominates the Tauri footprint. (Numbers are machine‑specific and meant as a ballpark; re‑run locally for your hardware.)

## Repository layout

```
data/    Python telemetry simulator → ride.db (shared data; gitignored)
rust/    Tauri + React + WebSocket dashboard
dotnet/  .NET solution — TelemetryPoc.Core (data) + TelemetryPoc.App (native WPF UI) + TelemetryPoc.App.Viz (pure UI logic) + TelemetryPoc.Map (offline MVT/Skia map)
docs/    specs, plans, and the dashboard reference target
```

The two apps share no code — each is its stack's idiomatic implementation. They share only `ride.db` and its schema.

## Quickstart

**1. Generate the data**
```bash
python data/simulate.py                                              # full 12h ride
python data/simulate.py --out data/ride_small.db --duration 60      # or a short one
```

**2a. Run the Rust app**
```bash
cd rust && npm install
RIDE_DB=../../data/ride_small.db RIDE_SPEED=5 npm run tauri dev
```

**2b. Run the .NET app** (.NET 8, Windows)
```bash
RIDE_SPEED=5 dotnet run --project dotnet/src/TelemetryPoc.App
```

Both connect to the same data and render the dashboard with the live HUD. See [`rust/README.md`](rust/README.md) and [`dotnet/README.md`](dotnet/README.md) for details and env vars (`RIDE_DB`, `RIDE_SPEED`, `RIDE_WS_PORT`).

**3. Offline map tiles (optional — for the basemap)**

Both maps render an **offline** vector basemap from a local `israel.mbtiles` tileset (gitignored, ~80 MB+). The **Rust** app auto-provisions it on first launch (download a prebuilt `.mbtiles`, else convert a geofabrik extract with `tilemaker`); without it the map falls back to the SVG track-only "grid" view. The **.NET** app does **not** auto-download — the file must already be present (`RIDE_MBTILES`, else `tiles/israel.mbtiles`); without it its map widget shows the dark background while the rest of the dashboard runs. To build the tileset (and, for Rust, label glyphs) yourself, see [`tiles/README.md`](tiles/README.md).

```bash
# build israel.mbtiles from a geofabrik OSM extract (see tiles/README.md for details)
cd tiles
wget https://download.geofabrik.de/asia/israel-and-palestine-latest.osm.pbf
tilemaker --input israel-and-palestine-latest.osm.pbf \
  --output israel.mbtiles --config config.json --process process.lua
./fetch-glyphs.sh                  # offline label glyphs → tiles/glyphs/
```

> **Windows:** use **tilemaker v2.4.0** with its own bundled resources — v3.0.0 crashes (`STATUS_STACK_BUFFER_OVERRUN`) and its `process.lua` needs a `Find` global v2.4 lacks.

## Tests

```bash
cd data && python -m pytest          # simulator
cd rust && npm test                  # frontend (vitest)
cd rust/src-tauri && cargo test      # Rust backend
cd dotnet && dotnet test             # .NET data layer
```

Chart/map/GUI code is build-verified; pure logic carries the unit coverage.

## Status

Both apps build, test, and run, and are fully reskinned to the INU dashboard. **Rust**: parameters, gauges, uPlot charts, offline MapLibre map, transport controls, interactive widget grid, perf HUD. **.NET**: native WPF parameters panel, gauges, ScottPlot.WPF charts, a native offline MVT/Skia map (`TelemetryPoc.Map`), transport pause/seek, interactive widget grid (drag / resize / toggle / remove, line zoom + hover, map pan / zoom / over-zoom), perf HUD. Final visual alignment against the reference is checked by launching each app (it can't be verified headless).

## License

See [LICENSE](LICENSE).
