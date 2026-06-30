# .NET native WPF Dashboard

The .NET implementation of the telemetry charting PoC. A **native WPF/XAML (MVVM)** application that replays a telemetry ride in-process and renders the INU-MONITOR dashboard — a grouped parameter table, radial gauges, scrolling strip charts, an offline map, and a perf HUD.

> Originally WPF + Blazor Hybrid; reskinned to native WPF/XAML to match the Rust INU dashboard for a fair native-vs-WebView perf comparison. The reskin is complete: parameters panel, gauges, ScottPlot.WPF charts, offline Skia map, perf HUD, read-only + interactive widget grid (drag / move / resize / gauge↔chart toggle / remove, line zoom + hover, map pan / zoom), and transport pause/seek.

## Architecture

- **Native WPF/XAML + MVVM**: no WebView, no JS. `TelemetryPoc.App` (WPF) + `TelemetryPoc.App.Viz` (net8.0 pure UI logic, xUnit-tested) + `TelemetryPoc.Map` (net8.0 offline MVT map renderer, xUnit-tested) + `TelemetryPoc.Core` (data layer).
- **In-process replay**: a `DispatcherTimer` advances the store on the UI thread; rendering is gated to new frames (the 10 Hz data cadence) — no WebSocket (the .NET-idiomatic transport).
- **Charts**: ScottPlot.WPF for realtime time-series strip charts (60s scrolling window, relative `m:ss` axis).
- **Map**: a **native MVT vector renderer** (`TelemetryPoc.Map` — `MbTilesReader` + protobuf decode + SkiaSharp draw) reading the same offline `israel.mbtiles` as the Rust app — no CDN/network, no Mapsui. The basemap is rasterised once per view into an `SKImage` and blitted each frame; pan/zoom/over-zoom (to street level, scaling z14 tiles) are interactive.
- **Layout**: the INU OVERVIEW screen (parameters panel + widget grid + perf HUD) per `docs/reference/dashboard-target.md`.

## Prerequisites

- **.NET 8.0+**, **Windows 10/11** (WPF is Windows-only), a display (no headless GUI).
- A ride database. Generate one with the Python simulator:
  ```bash
  python data/simulate.py                                            # full 12h data/ride.db
  python data/simulate.py --out data/ride_small.db --duration 10 --rate 10   # short fixture
  ```
- (Optional) an offline map tileset `tiles/israel.mbtiles` for the basemap — build it per [`../tiles/README.md`](../tiles/README.md). Unlike the Rust app, the .NET app does **not** auto-download the tileset: the file must already be present (resolved via `RIDE_MBTILES`, else `tiles/israel.mbtiles` walking up from the executable). Without it the map widget just renders the dark background (the rest of the dashboard runs normally). The label glyph PBFs (`tiles/glyphs/`) are a Rust/MapLibre need — the .NET Skia renderer draws text directly and does not use them.

## Running

From the project root:

```bash
dotnet run --project dotnet/src/TelemetryPoc.App
```

### Environment variables

- `RIDE_DB` — path to the ride database. If unset/missing, the app walks up from the executable dir for `data/ride.db`, then `data/ride_small.db`. (Use an absolute path to be safe.)
- `RIDE_SPEED` — replay speed multiplier (default `1.0`).
- `RIDE_MBTILES` — path to the offline map tileset. If unset, the app walks up from the executable dir for `tiles/israel.mbtiles`.

```bash
RIDE_SPEED=5 dotnet run --project dotnet/src/TelemetryPoc.App
RIDE_DB=D:\Projects\my\wpf-rust-compare-charts\data\ride.db dotnet run --project dotnet/src/TelemetryPoc.App
```

## Testing

```bash
cd dotnet && dotnet test     # xUnit: Core data layer + App.Viz pure UI logic
```

XAML views / ScottPlot / the Skia map host are build-verified + confirmed by launching the app; pure logic (`TelemetryPoc.App.Viz`, `TelemetryPoc.Map`, `TelemetryPoc.Core`) carries the unit coverage.

## Build

```bash
cd dotnet && dotnet build    # expect 0 errors, 0 warnings
```

## Notes

- The GUI requires a display; use the tests to verify logic headless.
- Build artifacts (`bin/`, `obj/`) are gitignored.
- The native WPF app runs fully offline (the map reads a local MBTiles file, not a CDN).
