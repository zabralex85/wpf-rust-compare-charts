# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A proof-of-concept that builds the **same realtime telemetry dashboard in two stacks** and compares their rendering performance. An INU-style dashboard (param table, scrolling strip charts, radial gauges, GPS map, perf HUD) is replayed from a shared SQLite "ride" and rendered by both a Rust app and a .NET app. The deliverable is the in-app HUD: FPS, frame time, end-to-end latency, CPU% (per-core), RAM.

The reference dashboard layout is `docs/reference/dashboard-target.md`.

## Layout

```
data/    Python telemetry simulator → ride.db (the shared data; gitignored). Source of truth for the schema.
rust/    Tauri 2 + React + TypeScript app. Rust backend streams the ride over a local WebSocket + serves offline map tiles; React renders it (uPlot charts, MapLibre map).
dotnet/  .NET solution: 4-ring onion — TelemetryPoc.Domain (entities + pure logic) → TelemetryPoc.Application (use cases + ports) → TelemetryPoc.Infrastructure (DB/tile/metrics adapters) → TelemetryPoc.Presentation (UI-shaping logic + Skia draw) + TelemetryPoc.App (Avalonia UI shell, Generic Host DI; cross-platform — runs on Linux/Windows/macOS).
tiles/   Offline vector basemap pipeline: build israel.mbtiles + glyphs (gitignored); committed fixture.mbtiles for tests. See tiles/README.md.
docs/    specs, plans (docs/superpowers/), and the dashboard reference target.
```

> The .NET solution uses a **4-ring onion architecture** (Domain → Application → Infrastructure → Presentation) with `TelemetryPoc.App` as the Avalonia UI composition root. **NetArchTest** rules enforce ring dependency directions. 174 xUnit tests span all rings; AXAML/charts/map are build-verified + launch-confirmed.

The two apps share **no code** — each is its stack's idiomatic implementation. They share only `ride.db` and the schema contract.

## The data contract (everything depends on this)

`data/` produces a SQLite DB read-only by both apps:
- `channels(id, name, column_name, unit, type, min, max, widget, display_order, addr)` — drives the UI; `widget` ∈ `strip|gauge|table|map_lat|map_lon`.
- `enum_values(channel_id, code, label, severity)` — e.g. `inu_mode2`: 0=Normal, 1=Critical.
- `samples(ts INTEGER PRIMARY KEY, <one column per channel in display order>)` — wide; `ts` = integer ms from ride start.
- `ride_meta(start_time, duration_s, rate_hz, channel_count)`.

Default ride: ~30 channels @ 10 Hz, 12h. A committed `data/ride_small.db` (10s) is the test fixture. **Frame `values` are index-aligned to `channels` sorted by `display_order`** — both apps rely on this.

## Architecture notes

- **Transport differs by design** (the realistic per-stack idiom): Rust replays over a local **WebSocket** (`ws://127.0.0.1:9001`) with JSON `meta`/`frame`/`metrics` messages; .NET replays **in-process** (no socket), a timer feeding the store on the UI dispatcher.
- **Mirrored data layer**: both stacks have the same logical pieces — DB reader, deterministic replay pacer, windowed strip-series buffer, enum/value formatting, telemetry store (O(1) latest, GPS track, partial-frame guard, re-meta reset), metrics sampler. Rust: `rust/src/data,ws,format,hud,gauge` (TS) + `rust/src-tauri/src` (backend). .NET: four onion rings — Domain (models/store/logic), Application (use cases/ports), Infrastructure (DB/tile/metrics adapters), Presentation (UI-shaping/Skia draw) — wired by Generic Host DI in `TelemetryPoc.App`.
- **Latency** = `now − emit_unix_ms` (Rust) / `now − store.LastEmitUnixMs` (.NET).
- **CPU%** is per-core in both (matching `sysinfo`) so the HUD numbers are comparable.
- **Offline map** (Rust): the backend runs an `axum` tile server (`127.0.0.1:9002`) that reads a local `israel.mbtiles` (rusqlite, TMS→XYZ Y-flip, gzip MVT) + serves glyph PBFs; MapLibre GL renders it with a dark INU style. On first launch the app auto-provisions the tileset (download a prebuilt `.mbtiles` via `RIDE_MBTILES_URL`, else `tilemaker`-convert a geofabrik extract); missing → SVG track-only "grid" view. The .NET app reads the same MBTiles with a native SkiaSharp MVT renderer (`TelemetryPoc.Infrastructure.MbTilesTileSource` + `TelemetryPoc.Presentation.BasemapRenderer`); no Mapsui. Tile/glyph build: `tiles/README.md`.

## Commands

**data/** (Python 3.11+, stdlib only; pytest for tests)
```
python data/simulate.py                 # full 12h ride.db
python data/simulate.py --out data/ride_small.db --duration 10 --rate 10   # short fixture
cd data && python -m pytest             # tests (pytest.ini disables the unused pytest-asyncio plugin)
```

**rust/** (Rust 1.95, Node 22; first cargo build is slow)
```
cd rust && npm install
npm test                                # frontend vitest (node + per-file jsdom)
cd rust/src-tauri && cargo test         # backend (incl. a ws integration test)
cd rust && npm run tauri dev            # launch app (env: RIDE_DB, RIDE_SPEED, RIDE_WS_PORT=9001, RIDE_TILES_PORT=9002)
```
Map env: `RIDE_MBTILES`/`RIDE_MBTILES_URL`/`RIDE_PBF_URL`/`RIDE_GLYPHS`/`RIDE_TILES_PORT`. Without a tileset the map shows the SVG grid view.

**tiles/** (offline basemap; gitignored outputs)
```
cd tiles
wget https://download.geofabrik.de/asia/israel-and-palestine-latest.osm.pbf
tilemaker --input israel-and-palestine-latest.osm.pbf --output israel.mbtiles --config config.json --process process.lua
./fetch-glyphs.sh                       # Noto Sans glyph PBFs → tiles/glyphs/ (offline labels)
python make_fixture.py                  # committed fixture.mbtiles for cargo tests
```
**Windows:** use **tilemaker v2.4.0** + its bundled resources (v3.0.0 crashes `STATUS_STACK_BUFFER_OVERRUN`; its `process.lua` needs a `Find` global v2.4 lacks). Labels use `name:latin` (Israel names are Hebrew; Noto Sans Regular has no Hebrew glyphs).

**dotnet/** (.NET 8; solution is `TelemetryPoc.slnx`)
```
cd dotnet && dotnet test                # xUnit (170 tests across all four rings; NetArchTest enforces ring boundaries; XAML UI is build-verify only)
dotnet build
dotnet run --project dotnet/src/TelemetryPoc.App   # launch the Avalonia app, cross-platform (env: RIDE_DB, RIDE_SPEED)
```
`RIDE_DB` defaults to an auto-resolved `data/ride.db` (or `ride_small.db`); generate a DB first. The Avalonia app runs offline (no CDN/network needed) on Linux/Windows/macOS. On Linux it needs the usual Avalonia/X11 deps (`libx11`, `libice`, `libsm`, `libfontconfig1`, GL) plus the .NET 8 runtime; fonts (IBM Plex) are embedded so text renders identically across OSes.

## Conventions

- This repo was built plan-by-plan with the superpowers workflow (`docs/superpowers/specs` + `plans`), one feature branch + PR per plan, subagent-driven TDD with a final whole-branch review before merge.
- Chart/map/GUI code (uPlot, MapLibre, ScottPlot.Avalonia, Avalonia/AXAML) is **build-verified, not unit-tested** — pure logic (TS modules; .NET Domain/Application/Infrastructure/Presentation) carries the test coverage; visual correctness is checked by launching the app against `docs/reference/dashboard-target.md`.
- Keep the two stacks behaviorally equivalent (same eviction boundary, rounding, latency model, metrics cadence) so the comparison stays fair.

## Status / not yet done

Both apps build, test, and run. The **Rust app** is fully reskinned to the INU dashboard (parameters, gauges, uPlot charts, offline MapLibre map, transport controls, frameless window, perf HUD). The **.NET app** is complete: **Avalonia UI** (AXAML) INU dashboard (parameters panel, gauges, ScottPlot.Avalonia charts, offline Skia MVT map via a Skia-canvas lease control, perf HUD, interactive widget grid, transport controls, frameless window) backed by a 4-ring onion architecture with Generic Host DI. It was **ported from WPF to Avalonia** (in place, `TelemetryPoc.App` only; the four inner rings are untouched) so the .NET app now runs cross-platform (Linux/Windows/macOS).

> **Benchmark note:** the .NET app now renders through **Skia** (Avalonia's backend), so the perf comparison is **Avalonia-Skia vs Rust-Skia** — it was WPF-DirectX vs Rust before the port. Keep this in mind when reading the HUD numbers.

Final visual alignment against the reference needs a live GUI launch (it can't be verified headless); on Linux this is verified on a Kali VM.
