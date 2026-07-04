# Rust Dioxus-native variant (Blitz/Vello) — design

**Date:** 2026-07-04
**Status:** approved (brainstorming), pending spec review → plan

## Goal

Add a **third** implementation of the telemetry dashboard: a Rust UI rendered
**natively** (no WebView) via **`dioxus-native`** (Blitz for HTML/CSS layout,
Vello/WGPU for GPU 2D graphics). Purpose: beat the WebView2 RAM floor that
dominates the current Tauri app and produce an honest third data point in the
README footprint comparison — **Rust-native (Vello) vs Rust-WebView (Tauri) vs
.NET-native (Skia)**.

The existing Tauri app must stay **untouched**.

## Why native (not `dioxus-desktop`)

`dioxus-desktop` uses `wry` = **WebView2** — the same Chromium runtime and
~300 MB multiprocess RAM floor as Tauri, so it offers **no memory win**. Only
`dioxus-native` (Blitz + Vello) bypasses the WebView and can beat that floor.
The cost: `uPlot` (canvas/JS) and `MapLibre` (WebGL) do **not** run under Blitz;
charts (and, later, the map) must be drawn natively.

Charting is feasible: `dioxus-native` exposes a **custom canvas element** with
`CustomPaintCtx`, letting us draw with Vello's `BezPath` (stroke polylines) —
exactly what a strip chart needs. Text/tables/HUD use ordinary HTML/CSS, which
is Blitz's core strength.

## Scope — v1 (minimum, de-risk the experimental renderer first)

Render just enough to get a **fair, representative footprint** while proving
Vello can drive live charts:

- **Parameter table** — latest value per channel (HTML/CSS text rows).
- **Strip charts** — one line chart per strip channel, 60 s scrolling window,
  drawn via `CustomPaintCtx` + Vello `BezPath` (dark background, cyan line —
  visually matching the other two apps).
- **Perf HUD** — FPS, frame time, CPU%, RAM (HTML/CSS).

**Explicitly out of v1** (later phases, tracked but not built now):
- GPS map (MVT → Vello) — the single largest effort; deferred.
- Radial gauges.
- Interactive drag/drop/resize widget grid.
- Transport controls (play/pause/seek).

Fixed layout, no interaction. This keeps the render workload representative
(text + live line charts) without UI plumbing that isn't perf-relevant.

## Isolation

- **New crate `rust-native/`** at the repo root — its own `Cargo.toml`, a
  standalone binary. **Zero changes to `rust/src-tauri`.**
- Reuses the existing backend logic by a **path dependency on `app_lib`**
  (`rust/src-tauri`, crate `app_lib`): `db`, `replay`, `metrics`, and the
  channel/frame types. No code is moved or refactored in this variant.
- New branch off `master`, its own PR. Independent of PR #62
  (GPS decimation + fuller grid).

**Known tradeoff:** `app_lib` also pulls Tauri/`wry`/`axum` dependencies (it
hosts `server.rs` and the Tauri command layer). Depending on it drags those
into `rust-native`'s build graph. Unused code is not allocated at runtime, so
the **footprint measurement stays honest** (no WebView is instantiated), but it
adds compile weight. **Fallback if it bites:** extract the pure data modules
(`db`, `replay`, `frame`, `metrics`, `tiles`) into a small `rust-core` crate
that both apps depend on. Not done in v1 to honor "zero changes to existing
code."

## Architecture & data flow

```
app_lib::db + app_lib::replay        (reused, in-process — no WebSocket)
        │  frames paced on a timer at the ride cadence (RIDE_SPEED honored)
        ▼
rust-native::data  (windowed strip buffer, 60 s — port of ringBuffer.ts;
                    + latest[] per channel)
        │
        ▼
rust-native::ui    (Dioxus RSX: param table + HUD + chart grid)
        │            charts pull the windowed buffer each frame
        ▼
rust-native::chart (CustomPaintCtx + Vello BezPath stroke)  ──► GPU

app_lib::metrics (sysinfo sampler)  ──►  HUD  (same CPU%/RAM as the other apps)
```

**Data feed:** in-process replay (mirrors the .NET app's idiom — no socket). A
timer reads paced samples from the reused `replay`/`db` and pushes them into the
windowed store. This keeps the data behavior identical to the other stacks
(same 60 s eviction window, same rounding) so the comparison stays fair.

## Modules (isolated units)

| File | Responsibility | Depends on |
|---|---|---|
| `rust-native/src/data.rs` | Windowed strip buffer (60 s evict) + `latest[]`. Pure logic. | app_lib frame/channel types |
| `rust-native/src/feed.rs` | In-process replay pacer; timer → store. | app_lib::replay, ::db |
| `rust-native/src/chart.rs` | Strip-chart geometry (samples → screen-space `BezPath` points) + Vello paint via `CustomPaintCtx`. | data.rs, vello |
| `rust-native/src/ui.rs` | Dioxus RSX: param table, HUD, fixed chart grid. | data.rs, chart.rs, metrics |
| `rust-native/src/main.rs` | Launch `dioxus-native` window; wire feed → store → UI; start metrics sampler. | all above, dioxus-native |

Boundary rule: `data.rs` and the geometry half of `chart.rs` are **pure and
unit-tested**; the Vello paint + RSX + window are **build-verified +
launch-confirmed** (same convention as the existing Tauri/Avalonia GUIs).

## Fairness (matches CLAUDE.md "keep the stacks behaviorally equivalent")

- Same 60 s strip-chart window / eviction boundary.
- Same value rounding/formatting where displayed.
- Same metrics cadence + source (`app_lib::metrics`, sysinfo) → comparable
  CPU%/RAM in the HUD.
- Same replay pacing (`RIDE_SPEED`, ride cadence).

## Measurement & README

Re-measure all three with the **same method** (Release, `RIDE_SPEED=1`, 30 s
warm-up, ~6 s window, 8-core Win11, RAM = working set). Add a third row to the
README "Measured footprint" table:

| Stack | Processes | RAM | CPU |
|---|---|---|---|
| .NET Avalonia (native Skia) | 1 | … | … |
| Rust Tauri (React in WebView2) | 7 | … | … |
| **Rust Dioxus-native (Blitz/Vello)** | 1 | *TBD by measurement* | *TBD* |

Note in the README that the native variant renders a **reduced dashboard**
(charts + params + HUD, no map/gauges yet) so the number is not a like-for-like
full-dashboard comparison until later phases land.

## Testing

```
cd rust-native && cargo test        # data.rs windowing, chart.rs geometry (pure)
cd rust-native && cargo run --release   # launch-verify the window renders charts
```

Pure logic carries unit coverage; Vello/RSX are build-verified + launch-
confirmed. No headless visual assertion (matches the repo convention).

## Risks

1. **`dioxus-native` is experimental** — API churn. Pin exact versions of
   `dioxus`, `dioxus-native`, `vello`, `blitz-*` in `Cargo.toml`; treat a
   breaking upgrade as a separate chore.
2. **`CustomPaintCtx` / Vello integration** — follow current upstream examples;
   the paint-callback signature is the main unknown. A spike in the first plan
   phase should render one static line before wiring live data.
3. **`app_lib` drags Tauri deps** — compile weight; fallback = `rust-core`
   extraction (see Isolation).
4. **Font parity** — load IBM Plex in Blitz to match the other apps' text; if
   Blitz font loading is limited, fall back to a bundled system font and note it.
5. **WGPU availability** — the native renderer needs a working GPU/WGPU backend
   on the test machine; verify on the target Win11 box early.

## Open questions deferred to later phases (not v1)

- Map: MVT tiles → Vello (reuse `app_lib::tiles` mbtiles reader + decode).
- Gauges, interactive grid, transport controls.
- Full-dashboard parity for a like-for-like footprint number.
