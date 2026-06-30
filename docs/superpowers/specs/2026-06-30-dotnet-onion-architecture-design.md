# .NET Onion Architecture + Production Patterns — Design Spec

**Date:** 2026-06-30
**Status:** Approved (user-directed)

## Goal

Restructure the .NET app to a **textbook onion / clean architecture** and add the
canonical .NET production idioms, as a **reference / learning demonstration**. The app
stays a perf-comparison PoC; the point is to show how a production-grade build would be
structured (rings, ports & adapters, DI, config, logging, lifecycle, enforced
boundaries) at the right scale — not architecture theater.

This is explicitly *not* a behavior change: the running dashboard, the Rust-vs-.NET
comparison, and the measured perf characteristics stay identical. It is a structural and
infrastructure refactor.

## Why (current state)

The current layering is already onion-shaped (`Core` depends on nothing → `App.Viz` /
`Map` → `App`), but the projects **mix rings**:

- `TelemetryPoc.Core` holds domain entities + logic (`Models`, `TelemetryStore`,
  `ChannelSeries`) **and** infrastructure (`TelemetryDb` sqlite reader,
  `MetricsSampler` sysinfo).
- `TelemetryPoc.Map` holds pure map math/decode (domain) **and** the `MbTilesReader`
  sqlite reader (infrastructure) **and** Skia rendering (presentation).
- Composition is `new` graphs in `MainWindow`; there is no DI, configuration is raw
  `Environment.GetEnvironmentVariable`, errors are swallowed (`RideSession.Error`, silent
  tile `catch`), there is no logging, and the `DispatcherTimer` / cached `MbTilesReader`
  are never disposed.

## Target structure (granularity "B": 4 rings, map cohesive)

Five source projects; dependencies point inward (`→` = references):

```
App (WPF shell, net8.0-windows)  → Presentation, Application, Infrastructure (compose only)
Infrastructure (net8.0)          → Application, Domain
Presentation (net8.0, pure)      → Application, Domain
Application (net8.0)             → Domain
Domain (net8.0)                  → (nothing)
```

### TelemetryPoc.Domain (entities + pure logic, zero dependencies)

- Telemetry: `Models` (`ChannelMeta`, `EnumValue`, `Sample`, `RideMeta`, `Metrics`),
  `TelemetryStore` (aggregate root), `ChannelSeries` (windowed buffer), `RideClock`
  (replay clock), `Severity` (value/band → severity classification rule).
- Map (pure math/geometry): `Region`, `MapFeature`, `WebMercator`, `MapProject`,
  `TileMath`, `TileProject`, `MapStyle`, `MapInteract`, `LabelLayout`.

No reference to SkiaSharp, sqlite, `Mapbox.VectorTile`, WPF, or any outer ring.
(`MvtBasemap` — the protobuf MVT decode — needs `Mapbox.VectorTile`, so it lives in
Infrastructure inside `MbTilesTileSource`, not in Domain.)

### TelemetryPoc.Application (use cases + ports)

- Use cases: `RideEngine` (replay orchestration), `RideData` (loaded-ride DTO),
  `ReplayPlayer`, `Pacer`.
- Ports (interfaces implemented by Infrastructure):
  - `IRideSource` — `Task<RideData> LoadAsync(CancellationToken)`
  - `IMetricsSampler` — `Metrics Sample()`
  - `ISystemClock` — `long UtcNowUnixMs { get; }`
  - `ITileSource` — `IReadOnlyList<MapFeature>? Decoded(int z, int x, int y)` (+ `IDisposable`)
  - `IRidePathResolver` — resolves ride-db and mbtiles paths

References Domain only.

### TelemetryPoc.Infrastructure (adapters)

- `SqliteRideSource : IRideSource` — the current `TelemetryDb` load logic (channels /
  enums / samples / meta) plus GPS-bounds computation via `Domain.MapProject`.
- `SysInfoMetricsSampler : IMetricsSampler` — the current `MetricsSampler`.
- `MbTilesTileSource : ITileSource` — the current `MbTilesReader` + `MvtBasemap` decode
  (cached read + gunzip + MVT-decode → Domain `MapFeature`s); uses `Mapbox.VectorTile`.
- `SystemClock : ISystemClock` — wraps `DateTimeOffset.UtcNow`.
- `RidePathResolver : IRidePathResolver` — the current `RidePaths` logic, reading
  `RideOptions`.

References Application + Domain; `Microsoft.Data.Sqlite`. Must not reference Presentation
or WPF.

### TelemetryPoc.Presentation (pure UI-shaping logic + Skia draw helpers)

WPF-free, xUnit-tested. Today's `App.Viz` plus the Map render bits:

- View-model support math: `GaugeViz`, `GaugeFormat`, `LineAxis`, `LineData`,
  `NearestSample`, `ParamGrouping`, `ParamRowView`, `StatusCounts`, `WidgetLayout`,
  `WidgetSeed`, `FpsMeter`, `HudFormat`, `HudMetrics`.
- Display formatting: `ValueFormat`, `MissionClock`.
- Skia draw helpers: `BasemapRenderer`, `TrackOverlay` (draw to an `SKCanvas`).

References Application + Domain; `SkiaSharp`. Must not reference Infrastructure or WPF.

### TelemetryPoc.App (WPF shell — net8.0-windows)

- `App.xaml.cs` — Generic Host composition root + DI registration.
- `MainWindow`, Views, ViewModels (constructor-injected).
- `RideSession` — thin host adapter: owns the `DispatcherTimer` and the wall-clock
  `Stopwatch`, pulls `RideEngine` + ports from DI, drives `RideEngine.Advance`.
- Skia WPF hosts (`MapWidgetView`, etc.).

Only `App` references WPF (`PresentationFramework` / `UseWPF`). It may reference
Infrastructure solely to register adapters in the container (the composition root).

### TelemetryPoc.Tests (consolidated, net8.0)

The 166 existing tests redistributed by ring namespace (logic unchanged): Domain tests
(store, series, formatting, map math), Application tests (`RideEngine`, replay),
Infrastructure tests (`SqliteRideSource` / `MbTiles` against `ride_small.db` /
`fixture.mbtiles`), Presentation tests (gauge / line / param / widget / hud). Plus the new
NetArchTest boundary rules.

## Cross-cutting production patterns

### DI / Generic Host

`App.xaml.cs` builds an `IHost` via `HostApplicationBuilder`. Registration:

- `services.Configure<RideOptions>(config...)`
- logging providers (Console + Debug)
- ports → adapters as singletons (`IRideSource`, `IMetricsSampler`, `ISystemClock`,
  `ITileSource`, `IRidePathResolver`)
- `RideEngine` (transient/factory — needs the loaded `RideData`), `RideSession`
  (singleton host adapter), all VMs, `MainWindow`

`OnStartup`: build + start host, resolve `MainWindow`, `Show()`. `OnExit`: dispose host
(stops the timer, disposes the tile source / sqlite).

### Configuration / Options

`appsettings.json` → strongly-typed `RideOptions { DbPath, Speed, MbTilesPath }` via
`IOptions<RideOptions>`. Environment variables `RIDE_DB` / `RIDE_SPEED` / `RIDE_MBTILES`
remain honored through a configuration provider (explicit mapping), so existing run
commands and the release bundle keep working. `IRidePathResolver` consumes the options
and the same walk-up fallbacks as today.

### Logging

`ILogger<T>` injected across rings. Replace silent failures with logs: ride load
(path / sample count / duration), load failure (was the swallowed `Error`), tile read /
decode failures (were silent `catch`), seek, and app lifecycle. Console + Debug providers.

### Lifecycle / async / error state

- `RideSession.StartAsync` loads the ride via `IRideSource.LoadAsync` off the UI thread
  (no UI block on a large DB); a loading state shows until the first frame.
- `RideSession : IDisposable` — stops the `DispatcherTimer` and disposes the tile source /
  sqlite on window close (`MainWindow.OnClosed` / host stop).
- Load failures surface to a UI error state (a bound property / overlay) instead of
  vanishing.

### Enforced boundaries (NetArchTest)

Rules asserted as unit tests:

- `Domain` has no dependency on Application / Infrastructure / Presentation / WPF /
  SkiaSharp / Microsoft.Data.Sqlite.
- `Application` depends only on Domain.
- `Infrastructure` does not depend on Presentation or WPF.
- `Presentation` does not depend on Infrastructure or WPF.
- Only `App` depends on WPF (`PresentationFramework`).

## Non-goals

- No behavior change: identical dashboard, replay, transport, map, HUD, and perf.
- No new features, channels, or screens.
- No change to the Rust app, the data simulator, or the tile pipeline.
- No deployment / hosting beyond the existing release workflow.
- No swap of charting/map/UI libraries.

## Testing

- All 166 current tests pass after redistribution (no logic change).
- New: NetArchTest boundary rules; `ISystemClock`-injected timing assertions where it
  tightens existing tests.
- `dotnet build` stays 0 warnings / 0 errors; the app launches and replays unchanged
  (XAML / Skia / ScottPlot remain build-verified + launch-confirmed per repo policy).

## Phased delivery (one branch + PR per phase)

The restructure is large mechanical churn; phase it so each step builds green:

1. **Domain + Application** — create the two inner projects, move entities / pure logic
   and use cases / ports; `Core`/`App.Viz`/`Map` reduced to what remains.
2. **Infrastructure** — adapters implementing the ports; delete the moved originals.
3. **Presentation** — rename / re-home the pure UI logic + Skia helpers.
4. **App: DI + Host + Config + Logging + lifecycle** — composition root, options,
   logging, async load, disposal, error state; VMs constructor-injected.
5. **Boundaries + tests** — NetArchTest rules; redistribute the test suite by ring.

Each phase ends with `dotnet build` (0/0), `dotnet test` green, and (phases 4–5) a launch
check.
