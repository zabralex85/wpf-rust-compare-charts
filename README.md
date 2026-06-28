# wpf-rust-compare-charts

Render the **same realtime telemetry dashboard in two stacks** and compare their performance.

An INU-style monitoring dashboard — live parameter table, scrolling strip charts, radial gauges, GPS map, and a perf HUD — is replayed from a shared SQLite "ride" and built twice:

| | Rust app | .NET app |
|---|---|---|
| Stack | Tauri 2 + React + TypeScript | native **WPF / XAML** (MVVM, C#) |
| Charts | uPlot (scrolling time-series) | ScottPlot.WPF |
| Map | MapLibre GL — **offline** vector basemap | Mapsui — **offline** MBTiles |
| Transport | local WebSocket | in-process |

This is a **paradigm contrast**: a web WebView UI (Tauri/React) vs a native retained-mode GPU UI (WPF) — each stack in its own idiom. Both read the **same `ride.db`** and show the same HUD — FPS, frame time, end-to-end latency, CPU% (per-core), RAM — so the two stacks can be compared head to head. Target layout: [`docs/reference/dashboard-target.md`](docs/reference/dashboard-target.md).

> The .NET app was originally WPF + Blazor Hybrid and is being reskinned to native WPF/XAML to match the Rust INU dashboard. The Rust app is fully reskinned; the .NET reskin is in progress (shell + parameters panel done; gauges / charts / map / HUD next).

## Repository layout

```
data/    Python telemetry simulator → ride.db (shared data; gitignored)
rust/    Tauri + React + WebSocket dashboard
dotnet/  .NET solution — TelemetryPoc.Core (data layer) + TelemetryPoc.App (WPF + Blazor UI)
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

Both maps render an **offline** vector basemap from a local `israel.mbtiles` tileset (gitignored, ~80 MB+). The Rust app auto-provisions it on first launch (download a prebuilt `.mbtiles`, else convert a geofabrik extract with `tilemaker`); without it the map falls back to the SVG track-only "grid" view. To build the tileset and label glyphs yourself, see [`tiles/README.md`](tiles/README.md).

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

Both apps build, test, and run. The **Rust app** is fully reskinned to the INU dashboard (parameters, gauges, uPlot charts, offline MapLibre map, transport controls, perf HUD). The **.NET app** is mid-reskin to native WPF (shell + parameters panel done; gauges / ScottPlot.WPF charts / Mapsui map / HUD next). Final visual alignment against the reference is checked by launching each app (it can't be verified headless).

## License

See [LICENSE](LICENSE).
