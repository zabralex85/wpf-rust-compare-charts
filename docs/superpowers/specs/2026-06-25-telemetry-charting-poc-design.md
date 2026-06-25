# Telemetry Charting PoC — Design Spec

**Date:** 2026-06-25
**Status:** Approved design, pending implementation plan

## 1. Goal

Build the **same realtime telemetry dashboard** (per the reference INU-style monitoring image) in two technology stacks and measure **realtime rendering performance** head-to-head. The headline deliverable is comparative perf numbers: FPS, frame time, end-to-end latency, CPU%, and RAM, captured live in each app.

The two stacks:

- **Rust:** Rust backend + Tauri + React + WebSocket
- **.NET:** C# + WPF + Blazor Hybrid (BlazorWebView)

Data is a pre-generated 12-hour ride stored in SQLite, replayed at speed into the charts.

## 2. Scope decisions (locked)

| Decision | Choice |
|---|---|
| Primary metric | Realtime render performance |
| Data rate | ~30 channels @ 10 Hz, 12h ride (~432k timestamp rows) |
| Feed model | Replay a pre-generated SQLite DB at speed (1×, with fast-forward) |
| DB schema | Wide samples table + `channels` metadata table |
| Simulator | Python script in `data/` |
| Metrics capture | In-app HUD overlay |
| Widget scope | Full dashboard replica (incl. GPS map with track + markers + tiles) |
| Chart libs | Stack-idiomatic each side |
| Structure | Independent apps sharing only `ride.db` (channel metadata read from DB) |
| Rust charts/map | uPlot + Leaflet |
| .NET charts/map | ScottPlot.Blazor + Leaflet (via JS interop) |

## 3. Repository structure

```
data/      Python simulator → ride.db (SQLite). Source of truth for the data.
rust/      Tauri app: Rust backend (rusqlite + WebSocket + sysinfo) + React/Vite UI (uPlot, Leaflet, HUD)
dotnet/    WPF app hosting BlazorWebView: C# replay service (Microsoft.Data.Sqlite)
           + Razor UI (ScottPlot.Blazor, Leaflet interop, HUD)
docs/      Specs and plans
```

- `ride.db` is **gitignored** (hundreds of MB); regenerate via `python data/simulate.py`.
- `data/simulate.py` is the committed source of truth for the data.
- A tiny `ride_small.db` (~10 s of data) is committed as a test fixture.

The two apps share **no code** — this keeps the perf comparison clean (each stack is purely itself). Each app reads the `channels` metadata table from `ride.db` at startup to learn the channel set.

## 4. Data — simulator and schema

**`data/simulate.py`** uses only the Python standard library (`sqlite3`). It generates a 12h @ 10 Hz ride with ~30 channels (~432k timestamp rows, ~13M cell values).

### Schema

**`channels`** — one row per telemetry channel; drives the entire UI.
- `id INTEGER PRIMARY KEY`
- `name TEXT` (e.g. `I0110Roll`)
- `unit TEXT`
- `type TEXT` — one of `real | enum | hex | text | time`
- `min REAL`, `max REAL`
- `widget TEXT` — one of `strip | gauge | table | map_lat | map_lon`
- `display_order INTEGER`
- `addr TEXT` (e.g. `I_01`, matching the image's Addr column)

**`enum_values`** — decode table for enum channels.
- `channel_id INTEGER`, `code INTEGER`, `label TEXT`, `severity TEXT`
- e.g. INUMode2: `0 = Normal (ok)`, `1 = Critical (red)`

**`samples`** — **wide** table.
- `ts INTEGER` — milliseconds from ride start (monotonic)
- one typed column per channel: `roll`, `pitch`, `heading_t`, `heading_m`, `acc_x/y/z`, `vel_x/y/z`, `plat_azim`, `alt_i`, `gcs_err`, `inu_mode1`, `inu_mode2`, `vclimb`, `sky_pitch`, `sky_roll`, `lat`, `lon`, …

**`ride_meta`** — single row: `start_time, duration_s, rate_hz, channel_count`.

### Channel set

Modeled on the reference image: Roll, Pitch, HeadingT/M, PlatAcc X/Y/Z, PlatVel X/Y/Z, PlatAzim, AltI, GCSErr, INUMode1, INUMode2 (enum Normal/Critical), Vclimb, SkyPitch, SkyRoll, GPS lat/lon, plus enough additional channels to reach ~30.

### Signal generation

Smooth, plausible motion: base sinusoids + bounded noise + occasional discrete events (e.g. INUMode2 flipping to Critical). GPS lat/lon trace a plausible track over the Tel Aviv area (matching the image's map). Inserts run in a single transaction with WAL enabled for speed.

## 5. Replay (native per stack — the measured pipeline)

Each app reads `ride.db` in `ts` order and paces sample emission to wall-clock time × a **speed factor** (default 1×, with a fast-forward control). Each emitted sample is stamped at emit time so the UI can compute end-to-end latency.

- **Rust:** a backend thread (rusqlite) reads the DB and serves frames over a **local WebSocket** server; the React UI connects via the browser `WebSocket` API. Frame format = JSON by default (binary/MessagePack as an optional flag).
- **.NET:** a C# replay service (Microsoft.Data.Sqlite) runs **in-process** and pushes samples via an observable/event into the Blazor components, which render with ScottPlot.Blazor. No socket — this is idiomatic Blazor Hybrid. JS interop is used only for Leaflet.

This transport asymmetry (cross-boundary WebSocket vs. in-process push) is intentional and reflects how each stack is realistically built.

## 6. Frontends (full replica of the image)

Both UIs use the same layout:

- **Left — param table:** ~30 live rows showing name, engineering value, a bar indicator, and Addr. Enum channels render with severity colors (Critical = red bar).
- **Center + bottom — strip charts:** scrolling time-series with a rolling ~60 s window.
- **Right — gauges + map:** radial gauges (e.g. SkyPitch, roll "SC") and a GPS map (Leaflet + online OSM tiles) showing the ride polyline, a current-position marker, and waypoint markers.

## 7. HUD overlay (the deliverable)

A corner overlay in each app shows, live:

- **FPS** and **frame time** (requestAnimationFrame delta)
- **End-to-end latency** (now − sample emit timestamp)
- **CPU%** and **RAM** of the app process — Rust via the `sysinfo` crate; .NET via `Process` / performance counters.

## 8. Error handling

- Missing or corrupt `ride.db` → UI message: "run `python data/simulate.py` first".
- Schema / channel mismatch → validated on load, clear error surfaced.
- Rust WebSocket disconnect → auto-reconnect with a status banner.
- Replay reaching end of ride → stop, with an optional loop toggle.

## 9. Testing

- **Python:** schema correctness, monotonic `ts`, expected row counts, value ranges — verified against a short (10 s) generation.
- **Rust:** replay pacing, WebSocket frame (de)serialization, DB read — against `ride_small.db`.
- **.NET:** replay service feed, ScottPlot data binding — against `ride_small.db`.
- **Shared fixture:** committed `ride_small.db` (~10 s) for fast tests.
- **Manual:** HUD numbers are sane; both apps visually match the reference image.

## 10. Defaults

- `ts` = integer milliseconds from ride start.
- Strip-chart window = 60 s.
- Rust WebSocket frame = JSON.

## 11. Non-goals (YAGNI)

- No live synthetic generation (replay only).
- No DB writes from the apps (read-only).
- No authentication; localhost only.
- No offline tile cache (online OSM tiles).
- Single ride file only.
