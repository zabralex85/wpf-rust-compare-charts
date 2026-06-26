# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A proof-of-concept that builds the **same realtime telemetry dashboard in two stacks** and compares their rendering performance. An INU-style dashboard (param table, scrolling strip charts, radial gauges, GPS map, perf HUD) is replayed from a shared SQLite "ride" and rendered by both a Rust app and a .NET app. The deliverable is the in-app HUD: FPS, frame time, end-to-end latency, CPU% (per-core), RAM.

The reference dashboard layout is `docs/reference/dashboard-target.md`.

## Layout

```
data/    Python telemetry simulator → ride.db (the shared data; gitignored). Source of truth for the schema.
rust/    Tauri 2 + React + TypeScript app. Rust backend streams the ride over a local WebSocket; React renders it.
dotnet/  .NET solution: TelemetryPoc.Core (net8.0 data layer) + TelemetryPoc.App (WPF + Blazor Hybrid UI).
docs/    specs, plans (docs/superpowers/), and the dashboard reference target.
```

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
- **Mirrored data layer**: both stacks have the same logical pieces — DB reader, deterministic replay pacer, windowed strip-series buffer, enum/value formatting, telemetry store (O(1) latest, GPS track, partial-frame guard, re-meta reset), metrics sampler. Rust: `rust/src/data,ws,format,hud,gauge` (TS) + `rust/src-tauri/src` (backend). .NET: `dotnet/src/TelemetryPoc.Core`.
- **Latency** = `now − emit_unix_ms` (Rust) / `now − store.LastEmitUnixMs` (.NET).
- **CPU%** is per-core in both (matching `sysinfo`) so the HUD numbers are comparable.

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
cd rust && npm run tauri dev            # launch app (env: RIDE_DB, RIDE_SPEED, RIDE_WS_PORT=9001)
```

**dotnet/** (.NET 8; solution is `TelemetryPoc.slnx`)
```
cd dotnet && dotnet test                # xUnit (Core data layer; UI is build-verify only)
dotnet build
dotnet run --project dotnet/src/TelemetryPoc.App   # launch WPF app (env: RIDE_DB, RIDE_SPEED)
```
`RIDE_DB` defaults to an auto-resolved `data/ride.db` (or `ride_small.db`); generate a DB first. The .NET UI and the Leaflet map need network (CDN + OSM tiles).

## Conventions

- This repo was built plan-by-plan with the superpowers workflow (`docs/superpowers/specs` + `plans`), one feature branch + PR per plan, subagent-driven TDD with a final whole-branch review before merge.
- Chart/map/GUI code (uPlot, ScottPlot.Blazor, Leaflet, WPF) is **build-verified, not unit-tested** — pure logic carries the test coverage; visual correctness is checked by launching the app against `docs/reference/dashboard-target.md`.
- Keep the two stacks behaviorally equivalent (same eviction boundary, rounding, latency model, metrics cadence) so the comparison stays fair.

## Status / not yet done

Both apps build, test, and run. The **visual alignment** of each dashboard against the reference image has not been done yet — it needs a live GUI launch (it can't be verified headless).
