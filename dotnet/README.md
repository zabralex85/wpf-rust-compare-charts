# .NET native WPF Dashboard

The .NET implementation of the telemetry charting PoC. A **native WPF/XAML (MVVM)** application that replays a telemetry ride in-process and renders the INU-MONITOR dashboard — a grouped parameter table, radial gauges, scrolling strip charts, an offline map, and a perf HUD.

> Originally WPF + Blazor Hybrid; reskinned to native WPF/XAML to match the Rust INU dashboard for a fair native-vs-WebView perf comparison. The reskin is complete: parameters panel, gauges, ScottPlot.WPF charts, offline Skia map, perf HUD, read-only + interactive widget grid (drag / move / resize / gauge↔chart toggle / remove, line zoom + hover, map pan / zoom), and transport pause/seek.

![.NET WPF INU-MONITOR dashboard](../docs/screenshots/dotnet-wpf.png)

## Architecture

The .NET solution is structured as a **4-ring onion architecture** with the WPF shell as the composition root.

- **`TelemetryPoc.Domain`** (net8.0, no external deps) — entities and pure logic: telemetry models, `TelemetryStore`, `ChannelSeries`, `RideClock`, `Severity`, `ValueFormat`, and all map math (projection, tile math, MVT geometry, styling, interaction).
- **`TelemetryPoc.Application`** (→ Domain) — use cases (`RideEngine`, `RideData`, `ReplayPlayer`, `Pacer`) and ports (`IRideSource`, `IMetricsSampler`, `ISystemClock`, `ITileSource`, `IRidePathResolver`).
- **`TelemetryPoc.Infrastructure`** (→ Application, Domain) — adapters: `SqliteRideSource` (ride DB), `MbTilesTileSource` (offline MVT tiles + protobuf decode), `SysInfoMetricsSampler`, `SystemClock`, `RidePathResolver`.
- **`TelemetryPoc.Presentation`** (net8.0, → Application, Domain; SkiaSharp) — pure UI-shaping logic (gauge/line/param/widget/hud helpers, `MissionClock`, `StatusCounts`) and Skia draw helpers (`BasemapRenderer`, `TrackOverlay`).
- **`TelemetryPoc.App`** (net8.0-windows, WPF) — the composition root: a `Microsoft.Extensions.Hosting` Generic Host wires Infrastructure adapters to Application ports via DI, with `IOptions<RideOptions>` config (appsettings.json + `RIDE_DB`/`RIDE_SPEED`/`RIDE_MBTILES` env), `ILogger` logging, async ride load, `IDisposable` lifecycle, and a load-error banner.
- **In-process replay**: a `DispatcherTimer` advances the store on the UI thread; rendering is gated to new frames (the 10 Hz data cadence) — no WebSocket (the .NET-idiomatic transport).
- **Charts**: ScottPlot.WPF for realtime time-series strip charts (60s scrolling window, relative `m:ss` axis).
- **Map**: a native MVT vector renderer (SkiaSharp) split across Presentation (`BasemapRenderer`, `TrackOverlay`) and Infrastructure (`MbTilesTileSource`) reads the same offline `israel.mbtiles` as the Rust app — no CDN/network, no Mapsui. The basemap is rasterised once per view into an `SKImage` and blitted each frame; pan/zoom/over-zoom (to street level, scaling z14 tiles) are interactive.
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

### Settings (`appsettings.json`)

The app is configured from the `Ride` section of `appsettings.json` — so a plain
`dotnet run` / F5 just works (no environment variables needed):

```json
"Ride": {
  "Speed": 5.0,        // replay speed multiplier
  "DbPath": "",        // ride DB; empty → walk up for data/ride.db, then data/ride_small.db
  "MbTilesPath": ""    // offline tileset; empty → walk up for tiles/israel.mbtiles
}
```

### Environment variables (optional overrides)

Each setting can be overridden at launch; an unset variable leaves the
`appsettings.json` value untouched:

- `RIDE_DB` → `Ride:DbPath`
- `RIDE_SPEED` → `Ride:Speed`
- `RIDE_MBTILES` → `Ride:MbTilesPath`

```bash
RIDE_SPEED=5 dotnet run --project dotnet/src/TelemetryPoc.App
RIDE_DB=D:\Projects\my\wpf-rust-compare-charts\data\ride.db dotnet run --project dotnet/src/TelemetryPoc.App
```

## Testing

```bash
cd dotnet && dotnet test     # xUnit: 170 tests spanning all four rings; NetArchTest rules enforce ring dependency directions
```

XAML views / ScottPlot / the Skia map host are build-verified + confirmed by launching the app; pure logic (Domain, Application, Infrastructure, Presentation) carries the unit coverage. **NetArchTest** boundary rules are part of the suite and fail the build if any ring imports something it should not.

## Build

```bash
cd dotnet && dotnet build    # expect 0 errors, 0 warnings
```

## Notes

- The GUI requires a display; use the tests to verify logic headless.
- Build artifacts (`bin/`, `obj/`) are gitignored.
- The native WPF app runs fully offline (the map reads a local MBTiles file, not a CDN).
- Ride samples are **streamed** from SQLite (a forward `ISampleCursor`), not loaded all at once, so memory stays flat regardless of ride length (a 12 h ride costs the same as a 10 min one).
