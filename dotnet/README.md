# .NET native WPF Dashboard

The .NET implementation of the telemetry charting PoC. A **native WPF/XAML (MVVM)** application that replays a telemetry ride in-process and renders the INU-MONITOR dashboard — a grouped parameter table, radial gauges, scrolling strip charts, an offline map, and a perf HUD.

> Originally WPF + Blazor Hybrid; being reskinned to native WPF/XAML to match the Rust INU dashboard for a fair native-vs-WebView perf comparison. Reskin in progress: shell + parameters panel done; gauges / charts / map / HUD next.

## Architecture

- **Native WPF/XAML + MVVM**: no WebView, no JS. `TelemetryPoc.App` (WPF) + `TelemetryPoc.App.Viz` (net8.0 pure UI logic, xUnit-tested) + `TelemetryPoc.Core` (data layer).
- **In-process replay**: a `DispatcherTimer` advances the store on the UI thread frame-by-frame — no WebSocket (the .NET-idiomatic transport).
- **Charts**: ScottPlot.WPF for realtime time-series strip charts (60s scrolling window, relative `m:ss` axis).
- **Map**: Mapsui (SkiaSharp) reading the same offline `israel.mbtiles` as the Rust app — no CDN/network.
- **Layout**: the INU OVERVIEW screen (parameters panel + widget grid + perf HUD) per `docs/reference/dashboard-target.md`.

## Prerequisites

- **.NET 8.0+**, **Windows 10/11** (WPF is Windows-only), a display (no headless GUI).
- A ride database. Generate one with the Python simulator:
  ```bash
  python data/simulate.py                                            # full 12h data/ride.db
  python data/simulate.py --out data/ride_small.db --duration 10 --rate 10   # short fixture
  ```
- (Optional) an offline map tileset `tiles/israel.mbtiles` for the basemap — see [`../tiles/README.md`](../tiles/README.md). Without it the map shows a track-only grid view.

## Running

From the project root:

```bash
dotnet run --project dotnet/src/TelemetryPoc.App
```

### Environment variables

- `RIDE_DB` — path to the ride database. If unset/missing, the app walks up from the executable dir for `data/ride.db`, then `data/ride_small.db`. (Use an absolute path to be safe.)
- `RIDE_SPEED` — replay speed multiplier (default `1.0`).

```bash
RIDE_SPEED=5 dotnet run --project dotnet/src/TelemetryPoc.App
RIDE_DB=D:\Projects\my\wpf-rust-compare-charts\data\ride.db dotnet run --project dotnet/src/TelemetryPoc.App
```

## Testing

```bash
cd dotnet && dotnet test     # xUnit: Core data layer + App.Viz pure UI logic
```

XAML views / ScottPlot / Mapsui are build-verified + confirmed by launching the app; pure logic carries the unit coverage.

## Build

```bash
cd dotnet && dotnet build    # expect 0 errors, 0 warnings
```

## Notes

- The GUI requires a display; use the tests to verify logic headless.
- Build artifacts (`bin/`, `obj/`) are gitignored.
- The native WPF app runs fully offline (the map reads a local MBTiles file, not a CDN).
