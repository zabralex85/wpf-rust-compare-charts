# .NET WPF INU-MONITOR Reskin — Design Spec

**Date:** 2026-06-28
**Status:** Approved (user-directed)

## Goal

Reskin the .NET app (`dotnet/src/TelemetryPoc.App`) to the same **INU-MONITOR**
dashboard the Rust app now has, so the two stacks render the same telemetry
workload and the in-app perf HUD (FPS / frame time / latency / CPU% / RAM)
compares fairly.

Crucially, the .NET app moves **off Blazor Hybrid to native WPF/XAML + MVVM**.
This makes the comparison a genuine paradigm contrast — native retained-mode
GPU UI (WPF) vs web WebView (Tauri React/uPlot canvas) — each stack in its own
idiom. The data layer `TelemetryPoc.Core` is UI-agnostic and **stays as is**.

Design source of truth: `docs/sample ui/INU Monitor (standalone-src).html` +
`docs/sample ui/screenshots/`, and the Rust reskin spec
`docs/superpowers/specs/2026-06-27-inu-monitor-rewrite-design.md` (mirror it for
fairness).

## Scope

**Perf-focused subset: the OVERVIEW screen + perf HUD only.**

In scope: the OVERVIEW dashboard rendering live data — grouped PARAMETERS panel,
gauge widgets, line-chart widgets, a map widget, a read-only transport footer,
and the perf HUD overlay — visually matching INU, updating in real time.

Out of scope (non-goals): the EVENTS and FLIGHT TRACK tabs; all editing
interactions (drag-add, drag-move, resize, gauge↔line toggle, remove,
line-zoom, hover tooltip); transport pause/seek; any change to
`TelemetryPoc.Core` or to the Rust app; new telemetry channels.

## Stack swap

`TelemetryPoc.App` is rewritten from Blazor Hybrid to native WPF:

- **Remove:** `Microsoft.AspNetCore.Components.WebView.Wpf`, `ScottPlot.Blazor`,
  `Components/*.razor`, `Main.razor`, `_Imports.razor`, `wwwroot/app.css`,
  BlazorWebView host wiring.
- **Add:** `ScottPlot.WPF` (MIT, realtime, full axis control, no animation
  overhead — chosen over LiveCharts2 to keep the perf baseline clean) and
  `Mapsui.Wpf` (MIT, SkiaSharp, reads MBTiles offline).
- Target stays `net8.0-windows`, `UseWPF=true`.

`TelemetryPoc.Core` is unchanged: `TelemetryDb`, `ReplayPlayer`, `Pacer`,
`ChannelSeries`, `TelemetryStore`, `MetricsSampler`, `ValueFormat`, `Models`.

## Architecture (MVVM)

Data flow stays **in-process** (no WebSocket — the .NET stack idiom): a
DispatcherTimer-based player advances the store on the WPF UI thread, then
ViewModels raise `PropertyChanged` (or push into the chart). The existing
cross-thread discipline is preserved — store `Advance` + metrics sampling run
on the UI thread (`Dispatcher`), never a background thread.

```
TelemetryPoc.App/
  App.xaml(.cs)                  — app bootstrap, resolves RIDE_DB, starts player
  MainWindow.xaml(.cs)           — window: TopBar + OverviewView + Hud overlay
  Resources/Theme.xaml           — INU palette + IBM Plex font resources
  Fonts/*.ttf                    — IBM Plex Sans + Mono (bundled resource)
  Views/
    TopBar.xaml                  — brand, OVERVIEW tab, alarm/caution/link, clock
    OverviewView.xaml            — PARAMETERS panel | widget grid | transport footer
    Widgets/GaugeView.xaml(.cs)  — Canvas-drawn gauge (arc, ticks, needle)
    Widgets/LineChartView.xaml   — ScottPlot.WPF realtime strip
    Widgets/MapWidgetView.xaml   — Mapsui basemap + GPS track
    Hud.xaml                     — overlay FPS/frame/latency/CPU/RAM
  ViewModels/
    OverviewViewModel            — owns store, exposes param groups + widget VMs
    ParamGroupViewModel / ParamRowViewModel
    GaugeViewModel / LineChartViewModel / MapWidgetViewModel
    TransportViewModel           — clock, T+, progress, buffer/samples/dropped
    HudViewModel                 — perf metrics
  Viz/                           — pure helpers (testable, no WPF types)
    ParamGrouping.cs             — column_name → group (mirror Rust groups.ts)
    GaugeGeometry.cs             — nice-round R, −135..+135° needle, tick angles
    LineAxis.cs                  — 60s scroll window + m:ss elapsed formatting
    MapProjection.cs             — lat/lon → Mapsui coords / track geometry
    HudFormat.cs                 — metric formatting
```

`Viz/` holds the pure logic so it is xUnit-tested without a UI; views are thin.

## OVERVIEW layout (fixed — no drag/resize)

- **TopBar:** `INU·MONITOR` brand + `AC 4X-ELT / FLT 1182`; single active tab
  `OVERVIEW`; right side `N ALARM` / `N CAUTION` pills (counts from severity),
  `LINK 1553B·OK`, clock (mission time + `T+`).
- **Left — PARAMETERS:** grouped live table of ~30 channels. Groups derived from
  `column_name` via a static table (INU Mode, Velocity, Attitude, Acceleration,
  Body Rates, System). Status dot per row (enum severity, else value-vs-band).
  Critical rows get the `#1a0e11` highlight. Header shows `ALL · 30 CH`.
- **Right — widget grid (fixed set, mirrors Rust `defaultWidgets`):**
  - 2 gauges — **SkyPitch**, **SkyRoll** (Canvas, auto-scale −135..+135°, ticks),
  - 2 line charts — **Roll**, **PlatAccZ** (ScottPlot.WPF, 60s window, `m:ss`
    axis, accent `#38c5e0`),
  - 1 map — Mapsui basemap + GPS track (large cell).
- **Footer — transport (read-only):** `T+mm:ss.mmm`, a progress bar over ride
  length, and `BUFFER / SAMPLES / DROPPED` counters. No pause/seek.
- **Window:** ordinary WPF chrome (frameless is Rust-specific; not needed here).

## Theme (exact INU values)

Background `#0a0e14`; panels `#10151d` / `#0c121a`; panel header `#131a24`;
borders `#1d2632` / `#1a2230`; column-header bg `#0c1119`. Text data `#c3ccd8` /
`#cdd6e1`; dim `#566273`; panel-title `#8b98a9`. Accents: cyan `#38c5e0`,
green `#2fd17a`, amber `#f5b440`, red `#ff4d52`; critical row bg `#1a0e11`.
Fonts: IBM Plex Sans (UI), IBM Plex Mono (numbers), bundled `.ttf`. Dark
scrollbars, 1px borders, 4–9px radii. All in `Theme.xaml`.

## Data wiring (live → UI), mirroring the Rust app

| Element | Source |
|---|---|
| Param rows / values | `store.Channels` + `store.Latest(id)` + `ValueFormat` |
| Param groups | static `column_name`→group table (mirror Rust `groups.ts`) |
| Status dot, alarm/caution counts | enum severity (`critical`→alarm) + configured bands (`caution`) |
| Gauge needle + auto-scale | `store.Latest(id)` + gauge math (nice-round R, −135..+135°) |
| Line series | `store.Series(id).Arrays()` (real `ChannelSeries`, 60s window) |
| Map track / position | `store.GpsTrack()` (lat/lon) → Mapsui projection |
| Clock / progress | mission time from `store.LastEmitUnixMs` + ride length |
| Perf HUD | `MetricsSampler` (FPS / frame ms / latency / CPU per-core / RAM) |

Charts use **ScottPlot.WPF** with a 60s scrolling window, a relative `m:ss`
elapsed x-axis, auto-ranged y, no animation, dark INU styling. The eviction /
window boundary stays identical to Rust (the Core `ChannelSeries` 60s window),
so render load is comparable.

Map uses **Mapsui**: an MBTiles tile layer (`RIDE_MBTILES`, resolving
`israel.mbtiles`), a GPS-track line plus a position marker, dark styling.
Offline.

## Perf HUD (the deliverable — parity with Rust)

FPS, frame time (ms), end-to-end latency (`now − store.LastEmitUnixMs`),
CPU% **per-core** (matching Rust's `sysinfo`), and RAM. Same sampling cadence
as Rust so the HUD numbers are directly comparable. Rendered as an overlay,
as in Rust.

## Testing

`TelemetryPoc.Core` xUnit suite stays. New pure logic in `Viz/` (param
grouping, gauge geometry, line/axis math, map projection, HUD formatting) is
xUnit-tested. WPF views + ScottPlot + Mapsui are **build-verified and confirmed
by launching the app** (XAML rendering can't be verified headless — same policy
as the Rust chart/GUI code). Each phase ends with a live visual check against
the INU screenshots.

## Phased delivery (one branch + PR per phase, subagent-driven TDD)

1. **Stack swap + shell** — csproj to native WPF (remove Blazor, add
   ScottPlot.WPF + Mapsui.Wpf), `Theme.xaml` + IBM Plex fonts, `MainWindow` +
   `TopBar` + empty `OverviewView`, DispatcherTimer player feeding the store on
   the UI thread. App launches and shows the themed shell with a live clock.
2. **Param panel** — grouped live PARAMETERS table with status dots, color
   states, critical highlight, `ALL · N CH`.
3. **Gauges + transport footer** — Canvas gauge (geometry + needle/ticks) wired
   to live values; read-only transport footer (clock, T+, progress, counters).
4. **Line charts** — ScottPlot.WPF realtime strip widgets (60s window, `m:ss`
   axis, accent), wired to `ChannelSeries`.
5. **Map** — Mapsui MBTiles basemap + live GPS track + position marker.
6. **HUD + polish** — perf HUD overlay (FPS/frame/latency/CPU/RAM), alarm/caution
   wiring, pixel pass against the screenshots.

## Non-goals

No EVENTS or FLIGHT TRACK tabs. No editing interactions (drag/resize/add/remove/
toggle/zoom/hover) and no transport pause/seek. No changes to
`TelemetryPoc.Core` or the Rust app. No new telemetry channels.
