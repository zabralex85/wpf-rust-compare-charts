# .NET Onion Architecture + Production Patterns Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure the .NET app into a 4-ring onion architecture (Domain / Application / Infrastructure / Presentation + WPF App shell) and add the .NET production idioms (DI/Generic Host, Options config, logging, async load + disposal + error state, NetArchTest boundary rules) — zero behavior change.

**Architecture:** Inner rings know nothing of outer rings. Application defines ports (interfaces); Infrastructure implements them; the WPF `App` is the composition root wiring adapters to ports via `Microsoft.Extensions.Hosting` DI. Pure UI-shaping logic + Skia draw helpers live in a WPF-free `Presentation` library so they stay unit-tested.

**Tech Stack:** .NET 8, WPF, xUnit, Microsoft.Extensions.{Hosting,DependencyInjection,Configuration,Logging,Options}, NetArchTest.Rules, Microsoft.Data.Sqlite, SkiaSharp, Mapbox.VectorTile, ScottPlot.WPF.

## Global Constraints

- Target frameworks: `net8.0` for all libraries, `net8.0-windows` (`UseWPF=true`) for `TelemetryPoc.App` only.
- **Zero behavior change**: identical dashboard, replay, transport, map, HUD, and perf. Pure structural + infrastructure refactor.
- Every phase ends green: `dotnet build dotnet/TelemetryPoc.slnx -c Debug` → **0 warnings / 0 errors**, `dotnet test` → all pass. Phases 4–5 also launch-confirm the app.
- StyleCop runs as warnings (must stay 0). Use 4-space indentation, file-scoped namespaces.
- Ring namespaces: `TelemetryPoc.Domain`, `TelemetryPoc.Application`, `TelemetryPoc.Infrastructure`, `TelemetryPoc.Presentation`, `TelemetryPoc.App`.
- Solution file is `dotnet/TelemetryPoc.slnx`; add every new project to it.
- Commands are run from the repo root; paths are relative to it unless absolute.

---

## File Structure

**New projects (src):**
- `dotnet/src/TelemetryPoc.Domain/` — entities + pure logic, zero deps.
- `dotnet/src/TelemetryPoc.Application/` — use cases + ports.
- `dotnet/src/TelemetryPoc.Infrastructure/` — adapters (sqlite, sysinfo, mbtiles, clock, paths).
- `dotnet/src/TelemetryPoc.Presentation/` — pure UI-shaping logic + Skia draw helpers (replaces `App.Viz`).

**Retired projects:** `TelemetryPoc.Core`, `TelemetryPoc.App.Viz`, `TelemetryPoc.Map` are emptied and removed once their files are re-homed.

**Kept:** `TelemetryPoc.App` (WPF shell, becomes the composition root), `tests/TelemetryPoc.Core.Tests` → renamed `tests/TelemetryPoc.Tests`.

**Ring placement of the current files** (file body unchanged unless a task says otherwise; only the `namespace` line changes to the ring namespace):

| Ring | Files (current → new project) |
|---|---|
| Domain | `Models`, `TelemetryStore`, `ChannelSeries`, `RideClock`, `Severity` (from Core/Viz); `Region`, `MapFeature`, `WebMercator`, `MapProject`, `TileMath`, `TileProject`, `MapStyle`, `MapInteract`, `LabelLayout` (from Map) |
| Application | `RideEngine`, `RideData`, `ReplayPlayer`, `Pacer` (from Core/Viz) + new ports |
| Infrastructure | `MetricsSampler`→`SysInfoMetricsSampler`, `TelemetryDb`→`SqliteRideSource`, `RidePaths`→`RidePathResolver`, `MbTilesReader`+`MvtBasemap`→`MbTilesTileSource`, new `SystemClock` |
| Presentation | `FpsMeter`, `GaugeFormat`, `GaugeViz`, `LineAxis`, `LineData`, `MissionClock`, `NearestSample`, `ParamGrouping`, `ParamRowView`, `StatusCounts`, `StreamTail`, `ValueFormat`, `WidgetLayout`, `WidgetSeed` (from Core/Viz); `BasemapRenderer`, `TrackOverlay` (from Map) |
| App | unchanged location: `App`, `MainWindow`, Views, ViewModels, `RideSession` (rewired to DI) |

> Note: `MvtBasemap` (MVT decode, uses `Mapbox.VectorTile`) and `MbTilesReader` (sqlite) both go to **Infrastructure**, merged behind `ITileSource`. This keeps `Mapbox.VectorTile` + `Microsoft.Data.Sqlite` out of Domain.

---

## Phase 1 — Domain + Application rings

### Task 1: Create the Domain project and move entities + pure logic

**Files:**
- Create: `dotnet/src/TelemetryPoc.Domain/TelemetryPoc.Domain.csproj`
- Move into `dotnet/src/TelemetryPoc.Domain/`: `Models.cs`, `TelemetryStore.cs`, `ChannelSeries.cs` (from Core); `RideClock.cs`, `Severity.cs` (from App.Viz); `Region.cs`, `MapFeature.cs`, `WebMercator.cs`, `MapProject.cs`, `TileMath.cs`, `TileProject.cs`, `MapStyle.cs`, `MapInteract.cs`, `LabelLayout.cs` (from Map)
- Modify: `dotnet/TelemetryPoc.slnx`

**Interfaces:**
- Produces: namespace `TelemetryPoc.Domain` containing all the above types with their current public signatures unchanged (e.g., `TelemetryStore`, `ChannelSeries`, `RideClock`, `MapProject.TrackBounds(...)`, `TileMath.VisibleTiles(...)`, `MvtGeomType`, `MapFeature`, etc.).

- [ ] **Step 1: Create the csproj**

```xml
<!-- dotnet/src/TelemetryPoc.Domain/TelemetryPoc.Domain.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Move the files and rename namespaces**

Move each listed file into `dotnet/src/TelemetryPoc.Domain/`, then change its `namespace` declaration to `TelemetryPoc.Domain`. These files currently live in `namespace TelemetryPoc.Core`, `TelemetryPoc.App.Viz`, or `TelemetryPoc.Map`.

```bash
cd dotnet/src/TelemetryPoc.Domain
git mv ../TelemetryPoc.Core/Models.cs ../TelemetryPoc.Core/TelemetryStore.cs ../TelemetryPoc.Core/ChannelSeries.cs .
git mv ../TelemetryPoc.App.Viz/RideClock.cs ../TelemetryPoc.App.Viz/Severity.cs .
git mv ../TelemetryPoc.Map/Region.cs ../TelemetryPoc.Map/MapFeature.cs ../TelemetryPoc.Map/WebMercator.cs ../TelemetryPoc.Map/MapProject.cs ../TelemetryPoc.Map/TileMath.cs ../TelemetryPoc.Map/TileProject.cs ../TelemetryPoc.Map/MapStyle.cs ../TelemetryPoc.Map/MapInteract.cs ../TelemetryPoc.Map/LabelLayout.cs .
sed -i -E 's/^namespace TelemetryPoc\.(Core|App\.Viz|Map);/namespace TelemetryPoc.Domain;/' *.cs
```

- [ ] **Step 3: Fix intra-Domain usings**

Some moved files reference siblings that were in a different namespace (e.g. `MapProject` uses `WebMercator`; `TileMath` uses `WebMercator`). Now all are `TelemetryPoc.Domain`, so remove any now-redundant `using TelemetryPoc.Map;` / `using TelemetryPoc.Core;` lines inside the moved files:

```bash
sed -i -E '/^using TelemetryPoc\.(Core|Map|App\.Viz);$/d' dotnet/src/TelemetryPoc.Domain/*.cs
```

- [ ] **Step 4: Register the project in the solution**

```bash
dotnet sln dotnet/TelemetryPoc.slnx add dotnet/src/TelemetryPoc.Domain/TelemetryPoc.Domain.csproj
```

- [ ] **Step 5: Build only Domain to verify it is self-contained**

Run: `dotnet build dotnet/src/TelemetryPoc.Domain/TelemetryPoc.Domain.csproj -c Debug --nologo`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. If a type is missing, it belongs to a later ring and was mis-moved — move it back.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "refactor(dotnet): add Domain ring (entities + pure logic)"
```

### Task 2: Create the Application project (use cases) and move RideEngine/ReplayPlayer

**Files:**
- Create: `dotnet/src/TelemetryPoc.Application/TelemetryPoc.Application.csproj`
- Move into `dotnet/src/TelemetryPoc.Application/`: `RideEngine.cs`, `RideData.cs` (from App.Viz); `ReplayPlayer.cs`, `Pacer.cs` (from Core)
- Modify: `dotnet/TelemetryPoc.slnx`

**Interfaces:**
- Consumes: `TelemetryPoc.Domain` (TelemetryStore, RideClock, Models, MapProject types used by RideData).
- Produces: `namespace TelemetryPoc.Application` with `RideEngine`, `RideData`, `ReplayPlayer`, `Pacer` (signatures unchanged).

- [ ] **Step 1: Create the csproj (references Domain)**

```xml
<!-- dotnet/src/TelemetryPoc.Application/TelemetryPoc.Application.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\TelemetryPoc.Domain\TelemetryPoc.Domain.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Move files + rename namespaces + fix usings**

```bash
cd dotnet/src/TelemetryPoc.Application
git mv ../TelemetryPoc.App.Viz/RideEngine.cs ../TelemetryPoc.App.Viz/RideData.cs .
git mv ../TelemetryPoc.Core/ReplayPlayer.cs ../TelemetryPoc.Core/Pacer.cs .
sed -i -E 's/^namespace TelemetryPoc\.(App\.Viz|Core);/namespace TelemetryPoc.Application;/' *.cs
# RideEngine/RideData used `using TelemetryPoc.Core;`; they now need Domain.
sed -i -E 's/^using TelemetryPoc\.Core;$/using TelemetryPoc.Domain;/' *.cs
sed -i -E '/^using TelemetryPoc\.App\.Viz;$/d' *.cs
```

- [ ] **Step 3: Add to solution**

```bash
dotnet sln dotnet/TelemetryPoc.slnx add dotnet/src/TelemetryPoc.Application/TelemetryPoc.Application.csproj
```

- [ ] **Step 4: Build Application**

Run: `dotnet build dotnet/src/TelemetryPoc.Application/TelemetryPoc.Application.csproj -c Debug --nologo`
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "refactor(dotnet): add Application ring (RideEngine, ReplayPlayer, Pacer)"
```

### Task 3: Define the ports in Application

**Files:**
- Create: `dotnet/src/TelemetryPoc.Application/Ports.cs`

**Interfaces:**
- Consumes: `TelemetryPoc.Domain` (`RideData` is in Application; `MapFeature`, `Metrics` are in Domain).
- Produces: `IRideSource`, `IMetricsSampler`, `ISystemClock`, `ITileSource`, `IRidePathResolver` in `namespace TelemetryPoc.Application`.

- [ ] **Step 1: Write the ports**

```csharp
// dotnet/src/TelemetryPoc.Application/Ports.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelemetryPoc.Domain;

namespace TelemetryPoc.Application;

/// <summary>Loads an entire ride (channels, enums, samples, meta, GPS bounds).</summary>
public interface IRideSource
{
    Task<RideData> LoadAsync(CancellationToken ct = default);
}

/// <summary>One CPU%/RAM sample of the current process, matching the Rust app's sysinfo.</summary>
public interface IMetricsSampler
{
    Metrics Sample();
}

/// <summary>The wall clock, injectable so latency/emit timestamps are testable.</summary>
public interface ISystemClock
{
    long UtcNowUnixMs { get; }
}

/// <summary>Offline basemap tiles: read + gunzip + MVT-decode, memoised.</summary>
public interface ITileSource : IDisposable
{
    IReadOnlyList<MapFeature>? Decoded(int z, int x, int y);
}

/// <summary>Resolves the ride DB and mbtiles paths from configuration + fallbacks.</summary>
public interface IRidePathResolver
{
    string ResolveRideDb();
    string? ResolveMbTiles();
}
```

- [ ] **Step 2: Rewire RideEngine to the `IMetricsSampler` port + drop the Application→Core reference**

Task 2 temporarily wired `RideEngine` to the concrete `MetricsSampler` (still in `Core`) and added an `Application→Core` ProjectReference — an onion violation. Fix it now. In `dotnet/src/TelemetryPoc.Application/RideEngine.cs`:
- delete the line `using TelemetryPoc.Core;`
- change `private readonly MetricsSampler _metrics;` → `private readonly IMetricsSampler _metrics;`
- change the ctor signature `RideEngine(RideData data, TelemetryStore store, MetricsSampler? metrics = null)` → `RideEngine(RideData data, TelemetryStore store, IMetricsSampler metrics)` (metrics is now a required injected dependency — Application must not `new` an infrastructure adapter)
- change `_metrics = metrics ?? new MetricsSampler();` → `_metrics = metrics;`

Then remove the Core ProjectReference from `dotnet/src/TelemetryPoc.Application/TelemetryPoc.Application.csproj` (delete the `<ProjectReference Include="..\TelemetryPoc.Core\TelemetryPoc.Core.csproj" />` line). Application now references **Domain only**.

- [ ] **Step 3: Build Application (Domain-only dependency)**

Run: `dotnet build dotnet/src/TelemetryPoc.Application/TelemetryPoc.Application.csproj -c Debug --nologo`
Expected: `0 Warning(s) 0 Error(s)`. If `MetricsSampler` is still referenced anywhere in Application, it was missed above.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "refactor(dotnet): Application ports + RideEngine depends on IMetricsSampler (drop Application→Core)"
```

> Downstream notes (handled in later tasks): `RideSession` (Task 9) already passes the DI-resolved `IMetricsSampler` into `new RideEngine(data, Store, _metrics)`. `RideEngineTests` currently relies on the old optional-metrics default — Task 15/17 must pass a tiny fake `IMetricsSampler` (returns `new Metrics(0, 0)`) into its `NewEngine()` helper.

---

## Phase 2 — Infrastructure ring (adapters)

### Task 4: Create Infrastructure and the metrics + clock + paths adapters

**Files:**
- Create: `dotnet/src/TelemetryPoc.Infrastructure/TelemetryPoc.Infrastructure.csproj`
- Create: `dotnet/src/TelemetryPoc.Infrastructure/SysInfoMetricsSampler.cs`, `SystemClock.cs`, `RidePathResolver.cs`
- Move: `dotnet/src/TelemetryPoc.Core/MetricsSampler.cs` → Infrastructure (its `Metrics` record moves to Domain), `dotnet/src/TelemetryPoc.App.Viz/RidePaths.cs` → Infrastructure
- Modify: `dotnet/TelemetryPoc.slnx`

**Interfaces:**
- Consumes: `TelemetryPoc.Application` (ports), `TelemetryPoc.Domain` (`Metrics`).
- Produces: `SysInfoMetricsSampler : IMetricsSampler`, `SystemClock : ISystemClock`, `RidePathResolver : IRidePathResolver`.

- [ ] **Step 1: Confirm the `Metrics` record is already in Domain**

Task 1 already extracted `Metrics` to `dotnet/src/TelemetryPoc.Domain/Metrics.cs` and removed it from `MetricsSampler.cs`. Verify with `grep -rn "record Metrics" dotnet/src` — it should appear only in `Domain/Metrics.cs`. No action needed here; proceed to Step 2.

- [ ] **Step 2: Create the csproj**

```xml
<!-- dotnet/src/TelemetryPoc.Infrastructure/TelemetryPoc.Infrastructure.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.8" />
    <PackageReference Include="Mapbox.VectorTile" Version="1.0.4-alpha2" NoWarn="NU1701" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\TelemetryPoc.Application\TelemetryPoc.Application.csproj" />
    <ProjectReference Include="..\TelemetryPoc.Domain\TelemetryPoc.Domain.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Move MetricsSampler, rename class to implement the port**

```bash
git mv dotnet/src/TelemetryPoc.Core/MetricsSampler.cs dotnet/src/TelemetryPoc.Infrastructure/SysInfoMetricsSampler.cs
```

Edit `SysInfoMetricsSampler.cs`: set `namespace TelemetryPoc.Infrastructure;`, add `using TelemetryPoc.Application; using TelemetryPoc.Domain;`, delete the `Metrics` record line (moved to Domain), and rename the class to `SysInfoMetricsSampler : IMetricsSampler` (keep the existing `Sample()` body verbatim).

- [ ] **Step 4: Write SystemClock**

```csharp
// dotnet/src/TelemetryPoc.Infrastructure/SystemClock.cs
using System;
using TelemetryPoc.Application;

namespace TelemetryPoc.Infrastructure;

public sealed class SystemClock : ISystemClock
{
    public long UtcNowUnixMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
```

- [ ] **Step 5: Move RidePaths → RidePathResolver**

```bash
git mv dotnet/src/TelemetryPoc.App.Viz/RidePaths.cs dotnet/src/TelemetryPoc.Infrastructure/RidePathResolver.cs
```

Rewrite it to implement the port, reading from `RideOptions` (Task 12 adds the type; for now resolve from env + walk-up exactly as today). Use this body, preserving the current resolution rules:

```csharp
// dotnet/src/TelemetryPoc.Infrastructure/RidePathResolver.cs
using System;
using System.IO;
using TelemetryPoc.Application;

namespace TelemetryPoc.Infrastructure;

public sealed class RidePathResolver : IRidePathResolver
{
    private readonly Func<string, bool> _exists;
    private readonly string _baseDir;

    public RidePathResolver(string baseDir, Func<string, bool> exists)
    {
        _baseDir = baseDir;
        _exists = exists;
    }

    public string ResolveRideDb()
    {
        var env = Environment.GetEnvironmentVariable("RIDE_DB");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        return WalkUp("data", "ride.db") ?? WalkUp("data", "ride_small.db")
            ?? Path.Combine(_baseDir, "data", "ride.db");
    }

    public string? ResolveMbTiles()
    {
        var env = Environment.GetEnvironmentVariable("RIDE_MBTILES");
        if (!string.IsNullOrWhiteSpace(env) && _exists(env)) return env;
        return WalkUp("tiles", "israel.mbtiles");
    }

    private string? WalkUp(string dir, string file)
    {
        var d = new DirectoryInfo(_baseDir);
        while (d is not null)
        {
            var p = Path.Combine(d.FullName, dir, file);
            if (_exists(p)) return p;
            d = d.Parent;
        }
        return null;
    }
}
```

> The original `RidePaths.Resolve` static method and its tests (`RidePathsTests`) are absorbed here; Task 17 updates the test to the new type.

- [ ] **Step 6: Add to solution + build Infrastructure**

```bash
dotnet sln dotnet/TelemetryPoc.slnx add dotnet/src/TelemetryPoc.Infrastructure/TelemetryPoc.Infrastructure.csproj
dotnet build dotnet/src/TelemetryPoc.Infrastructure/TelemetryPoc.Infrastructure.csproj -c Debug --nologo
```
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "refactor(dotnet): Infrastructure ring — metrics/clock/paths adapters + Metrics→Domain"
```

### Task 5: SqliteRideSource adapter (absorbs TelemetryDb)

**Files:**
- Move: `dotnet/src/TelemetryPoc.Core/TelemetryDb.cs` → `dotnet/src/TelemetryPoc.Infrastructure/SqliteRideSource.cs`

**Interfaces:**
- Consumes: `IRideSource`, `IRidePathResolver` (Application); `TelemetryPoc.Domain` (`MapProject.TrackBounds`, Models). The GPS-bounds logic moves here from the old `RideSession`.
- Produces: `SqliteRideSource : IRideSource`. Keeps the existing static loaders (`LoadChannels`/`LoadEnumValues`/`LoadSamples`/`LoadRideMeta`) as private helpers so `TelemetryDbTests`/`SampleReaderTests` can be repointed in Task 17.

- [ ] **Step 1: Move + rename**

```bash
git mv dotnet/src/TelemetryPoc.Core/TelemetryDb.cs dotnet/src/TelemetryPoc.Infrastructure/SqliteRideSource.cs
```

- [ ] **Step 2: Rewrite to implement IRideSource (keep the load helpers)**

Set `namespace TelemetryPoc.Infrastructure;`. Keep the four `Load*` methods (make them `internal static` so tests in the same assembly can call them). Add the adapter class wrapping them. Move the GPS-bounds computation out of the old `RideSession.LoadRide`/`GpsBoundsOf` into here:

```csharp
// dotnet/src/TelemetryPoc.Infrastructure/SqliteRideSource.cs (adapter portion)
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TelemetryPoc.Application;
using TelemetryPoc.Domain;

namespace TelemetryPoc.Infrastructure;

public sealed class SqliteRideSource : IRideSource
{
    private readonly IRidePathResolver _paths;

    public SqliteRideSource(IRidePathResolver paths) => _paths = paths;

    public Task<RideData> LoadAsync(CancellationToken ct = default) =>
        Task.Run(() => Load(_paths.ResolveRideDb()), ct);

    internal static RideData Load(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        var channels = LoadChannels(conn);
        var enums = LoadEnumValues(conn);
        var samples = LoadSamples(conn, channels);
        var meta = LoadRideMeta(conn);
        return new RideData(channels, enums, samples, meta.DurationS * 1000, GpsBounds(channels, samples));
    }

    private static (double, double, double, double)? GpsBounds(
        IReadOnlyList<ChannelMeta> channels, IReadOnlyList<Sample> samples)
    {
        int latIdx = -1, lonIdx = -1;
        for (int i = 0; i < channels.Count; i++)
        {
            if (channels[i].Widget == "map_lat") latIdx = i;
            if (channels[i].Widget == "map_lon") lonIdx = i;
        }
        if (latIdx < 0 || lonIdx < 0 || samples.Count == 0) return null;
        var lat = new double[samples.Count];
        var lon = new double[samples.Count];
        for (int i = 0; i < samples.Count; i++) { lat[i] = samples[i].Values[latIdx]; lon[i] = samples[i].Values[lonIdx]; }
        return MapProject.TrackBounds(lat, lon);
    }

    // ... keep the existing LoadChannels / LoadEnumValues / LoadSamples / LoadRideMeta bodies here,
    //     changed from `public static` to `internal static`.
}
```

- [ ] **Step 3: Build Infrastructure**

Run: `dotnet build dotnet/src/TelemetryPoc.Infrastructure/TelemetryPoc.Infrastructure.csproj -c Debug --nologo`
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "refactor(dotnet): SqliteRideSource adapter (absorbs TelemetryDb + GPS bounds)"
```

### Task 6: MbTilesTileSource adapter (absorbs MbTilesReader + MvtBasemap)

**Files:**
- Move: `dotnet/src/TelemetryPoc.Map/MbTilesReader.cs`, `dotnet/src/TelemetryPoc.Map/MvtBasemap.cs` → `dotnet/src/TelemetryPoc.Infrastructure/MbTilesTileSource.cs` (merge)

**Interfaces:**
- Consumes: `ITileSource` (Application), `TelemetryPoc.Domain` (`MapFeature`, `MvtGeomType`).
- Produces: `MbTilesTileSource : ITileSource` with `Decoded(int z, int x, int y)` (the memoised read+gunzip+decode) and `Dispose()`.

- [ ] **Step 1: Move + merge**

```bash
git mv dotnet/src/TelemetryPoc.Map/MbTilesReader.cs dotnet/src/TelemetryPoc.Infrastructure/MbTilesTileSource.cs
git rm dotnet/src/TelemetryPoc.Map/MvtBasemap.cs   # paste its DecodeTile body into MbTilesTileSource
```

Rename `MbTilesReader` → `MbTilesTileSource`, `namespace TelemetryPoc.Infrastructure;`, implement `ITileSource`, and inline the old `MvtBasemap.DecodeTile` as a private method (the `Decoded(z,x,y)` cache already calls it). Keep the existing read/gunzip/decode/cache bodies verbatim. The ctor takes the mbtiles path (resolved by the host from `IRidePathResolver`).

- [ ] **Step 2: Build Infrastructure**

Run: `dotnet build dotnet/src/TelemetryPoc.Infrastructure/TelemetryPoc.Infrastructure.csproj -c Debug --nologo`
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "refactor(dotnet): MbTilesTileSource adapter (absorbs MbTilesReader + MvtBasemap decode)"
```

---

## Phase 3 — Presentation ring

### Task 7: Create Presentation and move the pure UI-shaping logic

**Files:**
- Create: `dotnet/src/TelemetryPoc.Presentation/TelemetryPoc.Presentation.csproj`
- Move from App.Viz: `FpsMeter.cs`, `GaugeFormat.cs`, `GaugeViz.cs`, `LineAxis.cs`, `LineData.cs`, `MissionClock.cs`, `NearestSample.cs`, `ParamGrouping.cs`, `ParamRowView.cs`, `StatusCounts.cs`, `StreamTail.cs`, `WidgetLayout.cs`, `WidgetSeed.cs`
- Move from Map: `BasemapRenderer.cs`, `TrackOverlay.cs`

> NOTE (Task 1 correction): `ValueFormat` is **already in Domain** — `TelemetryStore.BuildEnumIndex` depends on it, so it cannot live in Presentation (that would be a Domain→Presentation violation). Do NOT move `ValueFormat`. `ParamRowView` / `StatusCounts` use it via `using TelemetryPoc.Domain;`. Likewise `ValueFormatTests` stays a Domain test.
- Modify: `dotnet/TelemetryPoc.slnx`

**Interfaces:**
- Consumes: `TelemetryPoc.Domain` (Models, Severity, map math), `TelemetryPoc.Application` (`ITileSource` — `BasemapRenderer` takes it instead of the old concrete reader), `SkiaSharp`.
- Produces: `namespace TelemetryPoc.Presentation` with all the above (signatures unchanged except `BasemapRenderer.Render` now takes `ITileSource`).

- [ ] **Step 1: Create the csproj**

```xml
<!-- dotnet/src/TelemetryPoc.Presentation/TelemetryPoc.Presentation.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SkiaSharp" Version="2.88.9" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\TelemetryPoc.Application\TelemetryPoc.Application.csproj" />
    <ProjectReference Include="..\TelemetryPoc.Domain\TelemetryPoc.Domain.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Move files + rename namespaces**

```bash
cd dotnet/src/TelemetryPoc.Presentation
git mv ../TelemetryPoc.App.Viz/FpsMeter.cs ../TelemetryPoc.App.Viz/GaugeFormat.cs ../TelemetryPoc.App.Viz/GaugeViz.cs ../TelemetryPoc.App.Viz/LineAxis.cs ../TelemetryPoc.App.Viz/LineData.cs ../TelemetryPoc.App.Viz/MissionClock.cs ../TelemetryPoc.App.Viz/NearestSample.cs ../TelemetryPoc.App.Viz/ParamGrouping.cs ../TelemetryPoc.App.Viz/ParamRowView.cs ../TelemetryPoc.App.Viz/StatusCounts.cs ../TelemetryPoc.App.Viz/StreamTail.cs ../TelemetryPoc.App.Viz/WidgetLayout.cs ../TelemetryPoc.App.Viz/WidgetSeed.cs .
git mv ../TelemetryPoc.Core/ValueFormat.cs .
git mv ../TelemetryPoc.Map/BasemapRenderer.cs ../TelemetryPoc.Map/TrackOverlay.cs .
sed -i -E 's/^namespace TelemetryPoc\.(App\.Viz|Core|Map);/namespace TelemetryPoc.Presentation;/' *.cs
sed -i -E 's/^using TelemetryPoc\.(App\.Viz|Core|Map);$/using TelemetryPoc.Domain;/' *.cs
# Collapse any duplicate `using TelemetryPoc.Domain;` lines left by the previous sed.
for f in *.cs; do awk '!(seen[$0]++ && /^using TelemetryPoc\.Domain;$/)' "$f" > "$f.tmp" && mv "$f.tmp" "$f"; done
```

- [ ] **Step 3: Repoint BasemapRenderer to ITileSource**

In `BasemapRenderer.cs`, change the `Render(SKCanvas, Region, MbTilesReader reader)` signature to `Render(SKCanvas canvas, Region region, ITileSource tiles)` and the call `reader.Decoded(...)` stays `tiles.Decoded(...)` (same method name on the port). Add `using TelemetryPoc.Application;`.

- [ ] **Step 4: Add to solution + build**

```bash
dotnet sln dotnet/TelemetryPoc.slnx add dotnet/src/TelemetryPoc.Presentation/TelemetryPoc.Presentation.csproj
dotnet build dotnet/src/TelemetryPoc.Presentation/TelemetryPoc.Presentation.csproj -c Debug --nologo
```
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Remove the now-empty old projects**

`TelemetryPoc.Core`, `TelemetryPoc.App.Viz`, `TelemetryPoc.Map` should now contain no `.cs` files (only `obj/bin`). Remove them:

```bash
dotnet sln dotnet/TelemetryPoc.slnx remove dotnet/src/TelemetryPoc.Core/TelemetryPoc.Core.csproj dotnet/src/TelemetryPoc.App.Viz/TelemetryPoc.App.Viz.csproj dotnet/src/TelemetryPoc.Map/TelemetryPoc.Map.csproj
git rm -r dotnet/src/TelemetryPoc.Core dotnet/src/TelemetryPoc.App.Viz dotnet/src/TelemetryPoc.Map
```

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "refactor(dotnet): Presentation ring (pure UI logic + Skia helpers); retire Core/App.Viz/Map"
```

---

## Phase 4 — App shell: DI, Host, Config, Logging, lifecycle

### Task 8: Repoint the App project references and namespaces

**Files:**
- Modify: `dotnet/src/TelemetryPoc.App/TelemetryPoc.App.csproj`
- Modify: every `.cs` in `dotnet/src/TelemetryPoc.App/` (usings)

**Interfaces:**
- Consumes: the four new rings.

- [ ] **Step 1: Replace project references + drop the dead Mapsui package**

In `TelemetryPoc.App.csproj`, replace the three old `ProjectReference`s with the four new rings and remove the unused `Mapsui.Wpf` package (the app uses the native Skia map now):

```xml
<ItemGroup>
  <ProjectReference Include="..\TelemetryPoc.Domain\TelemetryPoc.Domain.csproj" />
  <ProjectReference Include="..\TelemetryPoc.Application\TelemetryPoc.Application.csproj" />
  <ProjectReference Include="..\TelemetryPoc.Infrastructure\TelemetryPoc.Infrastructure.csproj" />
  <ProjectReference Include="..\TelemetryPoc.Presentation\TelemetryPoc.Presentation.csproj" />
</ItemGroup>
<ItemGroup>
  <PackageReference Include="ScottPlot.WPF" Version="5.0.55" />
  <PackageReference Include="SkiaSharp.Views.WPF" Version="2.88.9" />
  <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
</ItemGroup>
```

- [ ] **Step 2: Rewrite usings across the App**

The App's `.cs` and `.xaml.cs` files reference `TelemetryPoc.App.Viz`, `TelemetryPoc.Core`, `TelemetryPoc.Map`. Map them to the new rings:

```bash
cd dotnet/src/TelemetryPoc.App
grep -rl 'using TelemetryPoc\.\(App\.Viz\|Core\|Map\)' . --include=*.cs | while read f; do
  sed -i -E 's/using TelemetryPoc\.App\.Viz;/using TelemetryPoc.Presentation;/; s/using TelemetryPoc\.Core;/using TelemetryPoc.Domain;/; s/using TelemetryPoc\.Map;/using TelemetryPoc.Domain;/' "$f"
done
```

After this, fix any type that now lives in a different ring than the `using` added (e.g. `RideEngine`/`RideData` are in `TelemetryPoc.Application`; `ITileSource` in `TelemetryPoc.Application`; `BasemapRenderer`/`TrackOverlay`/`MissionClock`/widget+gauge+line+param helpers in `TelemetryPoc.Presentation`; `Region`/`MapProject`/`TileMath`/`MapInteract`/`MapStyle` in `TelemetryPoc.Domain`). Add the missing `using TelemetryPoc.Application;` / `using TelemetryPoc.Presentation;` where the compiler reports a missing type. **Do not** build-green yet — Task 9 rewrites `RideSession`, which is the main consumer.

- [ ] **Step 3: Commit (may not build yet)**

```bash
git add -A && git commit -m "refactor(dotnet): repoint App to the four rings; drop dead Mapsui.Wpf"
```

### Task 9: Rewrite RideSession as a DI-driven, disposable host adapter

**Files:**
- Modify: `dotnet/src/TelemetryPoc.App/RideSession.cs`

**Interfaces:**
- Consumes: `IRideSource`, `IMetricsSampler`, `ISystemClock`, `ITileSource`, `RideEngine`, `RideData`, `TelemetryStore`, `ILogger<RideSession>`.
- Produces: `RideSession` with `Store`, `RideMs`, `DurationMs`, `GpsBounds`, `IsPaused`, `Error`, events `MetaLoaded`/`Ticked`/`Reset`, `StartAsync()`, `Pause()`, `Resume()`, `Seek(double)`, and `Dispose()`.

- [ ] **Step 1: Rewrite RideSession**

```csharp
// dotnet/src/TelemetryPoc.App/RideSession.cs
using System;
using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using TelemetryPoc.Application;
using TelemetryPoc.Domain;

namespace TelemetryPoc.App;

/// <summary>WPF host for the replay: loads the ride via IRideSource (off the UI thread),
/// drives a RideEngine from a 30 Hz dispatcher timer, and exposes the engine state to the
/// view-models. Owns the timer + wall clock + lifecycle; all replay logic is in the engine.</summary>
public sealed class RideSession : IDisposable
{
    private readonly IRideSource _source;
    private readonly IMetricsSampler _metrics;
    private readonly ISystemClock _clock;
    private readonly ILogger<RideSession> _log;
    private readonly Stopwatch _sw = new();

    private RideEngine? _engine;
    private DispatcherTimer? _timer;
    private double _speed;
    private long _lastElapsed;

    public RideSession(IRideSource source, IMetricsSampler metrics, ISystemClock clock,
        ILogger<RideSession> log, RideOptions options)
    {
        _source = source;
        _metrics = metrics;
        _clock = clock;
        _log = log;
        _speed = options.Speed;
    }

    public TelemetryStore Store { get; } = new();
    public long DurationMs => _engine?.DurationMs ?? 0;
    public long RideMs => _engine?.RideMs ?? 0;
    public (double MinLat, double MinLon, double MaxLat, double MaxLon)? GpsBounds => _engine?.GpsBounds;
    public bool IsPaused => _engine?.IsPaused ?? true;

    private string? _error;
    public string? Error { get => _error; private set { _error = value; ErrorChanged?.Invoke(); } }

    public event Action? MetaLoaded;
    public event Action? Ticked;
    public event Action? Reset;
    public event Action? ErrorChanged;

    public async void StartAsync()
    {
        try
        {
            _log.LogInformation("Loading ride…");
            var data = await _source.LoadAsync().ConfigureAwait(true); // resume on UI thread
            _log.LogInformation("Ride loaded: {Samples} samples, {DurationMs} ms", data.Samples.Count, data.DurationMs);

            _engine = new RideEngine(data, Store, _metrics);
            _engine.Reset += () => Reset?.Invoke();
            MetaLoaded?.Invoke();

            _sw.Start();
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) => OnTick();
            _timer.Start();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ride load failed");
            Error = $"Ride load failed: {ex.Message}";
        }
    }

    private void OnTick()
    {
        var elapsed = _sw.ElapsedMilliseconds;
        var delta = elapsed - _lastElapsed;
        _lastElapsed = elapsed;
        if (_engine!.Advance(delta, _clock.UtcNowUnixMs, _speed)) Ticked?.Invoke();
    }

    public void Pause() => _engine?.Pause();
    public void Resume() => _engine?.Resume();

    public void Seek(double fraction)
    {
        if (_engine is null) return;
        _engine.Seek(fraction, _clock.UtcNowUnixMs);
        Ticked?.Invoke();
    }

    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
    }
}
```

> `RideEngine.Advance` already accepts `nowUnixMs`; the old direct `DateTimeOffset.UtcNow` calls are replaced by `_clock.UtcNowUnixMs`. `RideOptions` is defined in Task 12 (compile order: Task 12's file is added before this builds in Task 13).

- [ ] **Step 2: Commit (still not building until DI is wired)**

```bash
git add -A && git commit -m "refactor(dotnet): RideSession is a DI-driven disposable host adapter with logging + async load"
```

### Task 10: Inject the tile source into the map view-model

**Files:**
- Modify: `dotnet/src/TelemetryPoc.App/ViewModels/MapWidgetViewModel.cs`, `dotnet/src/TelemetryPoc.App/Views/MapWidgetView.xaml.cs`

**Interfaces:**
- Consumes: `ITileSource` (Application), `BasemapRenderer.Render(canvas, region, ITileSource)` (Presentation).
- Produces: `MapWidgetViewModel` exposing `ITileSource Tiles` instead of constructing/owning `MbTilesReader`.

- [ ] **Step 1: Replace the reader plumbing with the injected port**

In `MapWidgetViewModel.cs`, delete the `MbTilesReader`/`MbTilesPath`/`ResolveMbTiles` members and the `Reader` property; take an `ITileSource` (and `IRidePathResolver` is no longer needed here — the host constructs the tile source from the resolved path). Expose `public ITileSource Tiles { get; }` set from the constructor. In `MapWidgetView.xaml.cs.BuildBasemap`, call `BasemapRenderer.Render(rc, region, _vm.Tiles)`.

```csharp
// MapWidgetViewModel.cs — constructor + member
private readonly RideSession _session;
public ITileSource Tiles { get; }

public MapWidgetViewModel(RideSession session, ITileSource tiles)
{
    _session = session;
    Tiles = tiles;
}
```

> The tile source is a DI singleton (`IDisposable`), disposed by the container — not by the view. Remove any `_basemap`/reader disposal that touched the reader; keep the `SKImage` basemap disposal.

- [ ] **Step 2: Commit**

```bash
git add -A && git commit -m "refactor(dotnet): MapWidgetViewModel consumes ITileSource from DI"
```

### Task 11: Make DashboardViewModel build its map VM from DI

**Files:**
- Modify: `dotnet/src/TelemetryPoc.App/ViewModels/DashboardViewModel.cs`

**Interfaces:**
- Consumes: `ITileSource`.
- Produces: `DashboardViewModel(RideSession session, ITileSource tiles)` — passes `tiles` into the `MapWidgetViewModel` it creates.

- [ ] **Step 1: Thread the tile source through**

`DashboardViewModel` currently `new`s `MapWidgetViewModel(session)`. Change its constructor to `(RideSession session, ITileSource tiles)` and create `new MapWidgetViewModel(session, tiles)`. `OverviewViewModel` (which creates `DashboardViewModel`) likewise takes `ITileSource` and forwards it.

- [ ] **Step 2: Commit**

```bash
git add -A && git commit -m "refactor(dotnet): thread ITileSource through Dashboard/Overview VMs"
```

### Task 12: Add RideOptions + appsettings.json

**Files:**
- Create: `dotnet/src/TelemetryPoc.Application/RideOptions.cs`
- Create: `dotnet/src/TelemetryPoc.App/appsettings.json`
- Modify: `dotnet/src/TelemetryPoc.App/TelemetryPoc.App.csproj`

**Interfaces:**
- Produces: `RideOptions { string? DbPath; double Speed; string? MbTilesPath }` in `TelemetryPoc.Application`.

- [ ] **Step 1: Options type**

```csharp
// dotnet/src/TelemetryPoc.Application/RideOptions.cs
namespace TelemetryPoc.Application;

public sealed class RideOptions
{
    public string? DbPath { get; set; }
    public double Speed { get; set; } = 1.0;
    public string? MbTilesPath { get; set; }
}
```

- [ ] **Step 2: appsettings.json (copied to output)**

```json
{
  "Logging": { "LogLevel": { "Default": "Information" } },
  "Ride": { "Speed": 1.0 }
}
```

```xml
<!-- add to TelemetryPoc.App.csproj -->
<ItemGroup>
  <None Update="appsettings.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>
</ItemGroup>
```

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat(dotnet): RideOptions + appsettings.json"
```

### Task 13: Generic Host composition root in App.xaml.cs

**Files:**
- Modify: `dotnet/src/TelemetryPoc.App/App.xaml.cs`, `dotnet/src/TelemetryPoc.App/App.xaml` (remove `StartupUri`), `dotnet/src/TelemetryPoc.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: all rings + `Microsoft.Extensions.Hosting`.
- Produces: a built `IHost`; `MainWindow` resolved from DI; env `RIDE_DB`/`RIDE_SPEED`/`RIDE_MBTILES` bound into `RideOptions`.

- [ ] **Step 1: Remove StartupUri from App.xaml**

Delete `StartupUri="MainWindow.xaml"` from `App.xaml` (the host shows the window).

- [ ] **Step 2: Write the composition root**

```csharp
// dotnet/src/TelemetryPoc.App/App.xaml.cs
using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelemetryPoc.App.ViewModels;
using TelemetryPoc.Application;
using TelemetryPoc.Domain;
using TelemetryPoc.Infrastructure;

namespace TelemetryPoc.App;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var builder = Host.CreateApplicationBuilder();

        // Config: appsettings + env (RIDE_DB / RIDE_SPEED / RIDE_MBTILES → RideOptions)
        builder.Configuration.AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
        {
            ["Ride:DbPath"] = Environment.GetEnvironmentVariable("RIDE_DB"),
            ["Ride:MbTilesPath"] = Environment.GetEnvironmentVariable("RIDE_MBTILES"),
            ["Ride:Speed"] = Environment.GetEnvironmentVariable("RIDE_SPEED"),
        });
        builder.Services.Configure<RideOptions>(builder.Configuration.GetSection("Ride"));
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<RideOptions>>().Value);

        builder.Logging.AddDebug();

        // Ports → adapters
        builder.Services.AddSingleton<IRidePathResolver>(_ =>
            new RidePathResolver(AppContext.BaseDirectory, File.Exists));
        builder.Services.AddSingleton<IRideSource, SqliteRideSource>();
        builder.Services.AddSingleton<IMetricsSampler, SysInfoMetricsSampler>();
        builder.Services.AddSingleton<ISystemClock, SystemClock>();
        builder.Services.AddSingleton<ITileSource>(sp =>
        {
            var path = sp.GetRequiredService<IRidePathResolver>().ResolveMbTiles();
            return new MbTilesTileSource(path); // null path → empty tile source (map shows background)
        });

        // App graph
        builder.Services.AddSingleton<RideSession>();
        builder.Services.AddSingleton<TopBarViewModel>();
        builder.Services.AddSingleton<OverviewViewModel>();
        builder.Services.AddSingleton<TransportViewModel>();
        builder.Services.AddSingleton<HudViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();
        _host.Start();

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
```

> `MbTilesTileSource` must accept a nullable path and degrade to returning `null` features when the file is absent (the map then shows the dark background — current behavior). Adjust its ctor in Task 6 accordingly if not already.

- [ ] **Step 3: Inject the VMs into MainWindow**

```csharp
// MainWindow.xaml.cs — constructor
private readonly RideSession _session;

public MainWindow(RideSession session, TopBarViewModel topBar, OverviewViewModel overview,
    TransportViewModel transport, HudViewModel hud)
{
    InitializeComponent();
    _session = session;
    TopBar.DataContext = topBar;
    Overview.DataContext = overview;
    Transport.DataContext = transport;
    Hud.DataContext = hud;
    Loaded += (_, _) => _session.StartAsync();
    Closed += (_, _) => _session.Dispose();
    SourceInitialized += OnSourceInitialized; // keep the WM_GETMINMAXINFO hook
}
```

(Keep the existing Win32 `OnSourceInitialized`/`WindowProc`/`ClampToWorkArea` members.)

- [ ] **Step 4: Build the App project (transitively builds all 5 src rings)**

Run: `dotnet build dotnet/src/TelemetryPoc.App/TelemetryPoc.App.csproj -c Debug --nologo`
Expected: `0 Warning(s) 0 Error(s)`. Fix any remaining missing-`using` from Task 8 here.

> Do NOT build `dotnet/TelemetryPoc.slnx` yet — the test project still references the retired `Core`/`App.Viz`/`Map` projects and won't compile until Task 15. Building the App project compiles all five src rings without the (transiently broken) tests.

- [ ] **Step 5: Launch-confirm**

Run: `RIDE_DB=data/ride.db RIDE_MBTILES=tiles/israel.mbtiles RIDE_SPEED=5 dotnet run --project dotnet/src/TelemetryPoc.App -c Debug` — confirm the dashboard replays (params, gauges, charts, map+track, transport, HUD) exactly as before, then close it.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(dotnet): Generic Host composition root — DI, options, logging, lifecycle"
```

### Task 14: Surface the load-error state in the UI

**Files:**
- Modify: `dotnet/src/TelemetryPoc.App/MainWindow.xaml`, `dotnet/src/TelemetryPoc.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `RideSession.Error`, `RideSession.ErrorChanged`.

- [ ] **Step 1: Add an error overlay**

Add a top-level `TextBlock` (collapsed by default) over the main grid in `MainWindow.xaml`:

```xml
<TextBlock x:Name="ErrorBanner" Visibility="Collapsed" Foreground="{StaticResource Red}"
           Background="{StaticResource Panel2}" Padding="12,8" FontFamily="{StaticResource MonoFont}"
           HorizontalAlignment="Center" VerticalAlignment="Top" Panel.ZIndex="100" />
```

In `MainWindow.xaml.cs` ctor, subscribe:

```csharp
_session.ErrorChanged += () => Dispatcher.Invoke(() =>
{
    ErrorBanner.Text = _session.Error;
    ErrorBanner.Visibility = _session.Error is null ? Visibility.Collapsed : Visibility.Visible;
});
```

- [ ] **Step 2: Build + commit**

Run: `dotnet build dotnet/TelemetryPoc.slnx -c Debug --nologo` → `0/0`.

```bash
git add -A && git commit -m "feat(dotnet): surface ride-load failure as a UI error banner"
```

---

## Phase 5 — Boundaries + test redistribution

### Task 15: Rename the test project and repoint references

**Files:**
- Rename: `dotnet/tests/TelemetryPoc.Core.Tests/` → `dotnet/tests/TelemetryPoc.Tests/`
- Modify: the `.csproj` references; `dotnet/TelemetryPoc.slnx`

**Interfaces:**
- Consumes: the four rings.

- [ ] **Step 1: Rename the folder + csproj**

```bash
git mv dotnet/tests/TelemetryPoc.Core.Tests dotnet/tests/TelemetryPoc.Tests
git mv dotnet/tests/TelemetryPoc.Tests/TelemetryPoc.Core.Tests.csproj dotnet/tests/TelemetryPoc.Tests/TelemetryPoc.Tests.csproj
```

- [ ] **Step 2: Repoint references (4 rings + NetArchTest)**

Replace the three old `ProjectReference`s with the four rings and add NetArchTest:

```xml
<ItemGroup>
  <PackageReference Include="NetArchTest.Rules" Version="1.3.2" />
</ItemGroup>
<ItemGroup>
  <ProjectReference Include="..\..\src\TelemetryPoc.Domain\TelemetryPoc.Domain.csproj" />
  <ProjectReference Include="..\..\src\TelemetryPoc.Application\TelemetryPoc.Application.csproj" />
  <ProjectReference Include="..\..\src\TelemetryPoc.Infrastructure\TelemetryPoc.Infrastructure.csproj" />
  <ProjectReference Include="..\..\src\TelemetryPoc.Presentation\TelemetryPoc.Presentation.csproj" />
</ItemGroup>
```

Update the solution: `dotnet sln dotnet/TelemetryPoc.slnx remove` the old test csproj path and `add` the new one.

- [ ] **Step 3: Fix test namespaces/usings**

The tests use `using TelemetryPoc.Core;`, `TelemetryPoc.App.Viz;`, `TelemetryPoc.Map;`. Map them to the ring that now owns each tested type:

```bash
cd dotnet/tests/TelemetryPoc.Tests
sed -i -E 's/using TelemetryPoc\.Core;/using TelemetryPoc.Domain;/; s/using TelemetryPoc\.Map;/using TelemetryPoc.Domain;/; s/using TelemetryPoc\.App\.Viz;/using TelemetryPoc.Presentation;/' *.cs
```

Then per failing test, add the correct ring `using` for types that moved elsewhere: `RideEngineTests`/`ReplayPlayerTests`/`PacerTests` → `using TelemetryPoc.Application;`; `TelemetryDbTests`/`SampleReaderTests`/`MetricsSamplerTests`/`MbTilesReaderTests`/`RidePathsTests` → `using TelemetryPoc.Infrastructure;` (and update to the new type names — Task 16/17).

`RideEngine`'s metrics parameter is now a required `IMetricsSampler` (Task 3), so `RideEngineTests` must supply one. Add a tiny fake at the top of `RideEngineTests.cs` and pass it into the `NewEngine()`/`new RideEngine(...)` calls:

```csharp
private sealed class FakeMetrics : TelemetryPoc.Application.IMetricsSampler
{
    public TelemetryPoc.Domain.Metrics Sample() => new(0, 0);
}
// e.g. private static RideEngine NewEngine() => new(Ride(), new TelemetryStore(), new FakeMetrics());
```

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "refactor(dotnet): rename test project to TelemetryPoc.Tests, repoint to rings + NetArchTest"
```

### Task 16: Fix the Infrastructure-facing tests to the new types

**Files:**
- Modify: `dotnet/tests/TelemetryPoc.Tests/TelemetryDbTests.cs`, `SampleReaderTests.cs`, `MetricsSamplerTests.cs`, `MbTilesReaderTests.cs`, `RidePathsTests.cs`, `Fixtures.cs`

**Interfaces:**
- Consumes: `SqliteRideSource.Load`/`internal` loaders, `SysInfoMetricsSampler`, `MbTilesTileSource`, `RidePathResolver`.

- [ ] **Step 1: Repoint each**

- `TelemetryDbTests`/`SampleReaderTests`: call `SqliteRideSource.LoadChannels/LoadSamples/...` (now `internal static`; the test assembly sees them via `InternalsVisibleTo` — add `[assembly: InternalsVisibleTo("TelemetryPoc.Tests")]` to Infrastructure, or make the loaders `public static`). Choose **public static** loaders to avoid the attribute.
- `MetricsSamplerTests`: `new SysInfoMetricsSampler().Sample()`.
- `MbTilesReaderTests`: `new MbTilesTileSource(fixturePath).Decoded(z,x,y)`.
- `RidePathsTests`: `new RidePathResolver(baseDir, exists).ResolveRideDb()/ResolveMbTiles()` — adjust assertions to the new instance API.

> If making the `SqliteRideSource` loaders `public static`, update Task 5 Step 2 to `public static` (consistency note).

- [ ] **Step 2: Run only the repointed tests**

Run: `dotnet test dotnet/tests/TelemetryPoc.Tests/TelemetryPoc.Tests.csproj -c Debug --filter "FullyQualifiedName~Telemetry|FullyQualifiedName~Sample|FullyQualifiedName~Metrics|FullyQualifiedName~MbTiles|FullyQualifiedName~RidePath"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "test(dotnet): repoint infrastructure tests to the new adapter types"
```

### Task 17: Full test run + the rest of the suite

- [ ] **Step 1: Build + run the whole suite**

Run: `dotnet build dotnet/TelemetryPoc.slnx -c Debug --nologo && dotnet test dotnet/TelemetryPoc.slnx -c Debug --no-build`
Expected: `0 Warning(s) 0 Error(s)` and **all 166 tests pass** (same count as before; only relocated).

- [ ] **Step 2: Fix any stragglers**

For any failing test, the cause is a wrong ring `using` — add the correct one (`TelemetryPoc.Domain`/`Application`/`Presentation`/`Infrastructure`). No assertion logic changes.

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "test(dotnet): green after ring redistribution (166 tests)"
```

### Task 18: Architecture-boundary tests (NetArchTest)

**Files:**
- Create: `dotnet/tests/TelemetryPoc.Tests/ArchitectureTests.cs`

**Interfaces:**
- Consumes: `NetArchTest.Rules`, one type per ring to anchor the assembly (e.g. `TelemetryStore`, `RideEngine`, `SqliteRideSource`, `GaugeViz`).

- [ ] **Step 1: Write the boundary rules**

```csharp
// dotnet/tests/TelemetryPoc.Tests/ArchitectureTests.cs
using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace TelemetryPoc.Tests;

public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(TelemetryPoc.Domain.TelemetryStore).Assembly;
    private static readonly Assembly Application = typeof(TelemetryPoc.Application.RideEngine).Assembly;
    private static readonly Assembly Infrastructure = typeof(TelemetryPoc.Infrastructure.SqliteRideSource).Assembly;
    private static readonly Assembly Presentation = typeof(TelemetryPoc.Presentation.GaugeViz).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_outward()
    {
        var result = Types.InAssembly(Domain)
            .Should().NotHaveDependencyOnAny(
                "TelemetryPoc.Application", "TelemetryPoc.Infrastructure",
                "TelemetryPoc.Presentation", "TelemetryPoc.App",
                "Microsoft.Data.Sqlite", "SkiaSharp", "PresentationFramework")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? new System.Collections.Generic.List<string>()));
    }

    [Fact]
    public void Application_depends_only_on_Domain()
    {
        var result = Types.InAssembly(Application)
            .Should().NotHaveDependencyOnAny(
                "TelemetryPoc.Infrastructure", "TelemetryPoc.Presentation", "TelemetryPoc.App")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? new System.Collections.Generic.List<string>()));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_Presentation_or_WPF()
    {
        var result = Types.InAssembly(Infrastructure)
            .Should().NotHaveDependencyOnAny("TelemetryPoc.Presentation", "TelemetryPoc.App", "PresentationFramework")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? new System.Collections.Generic.List<string>()));
    }

    [Fact]
    public void Presentation_does_not_depend_on_Infrastructure_or_WPF()
    {
        var result = Types.InAssembly(Presentation)
            .Should().NotHaveDependencyOnAny("TelemetryPoc.Infrastructure", "TelemetryPoc.App", "PresentationFramework")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? new System.Collections.Generic.List<string>()));
    }
}
```

- [ ] **Step 2: Run the architecture tests**

Run: `dotnet test dotnet/tests/TelemetryPoc.Tests/TelemetryPoc.Tests.csproj -c Debug --filter "FullyQualifiedName~ArchitectureTests"`
Expected: 4 PASS. A failure prints the offending type names — fix the misplaced reference, don't weaken the rule.

- [ ] **Step 3: Full build + test + launch**

Run: `dotnet build dotnet/TelemetryPoc.slnx -c Debug --nologo && dotnet test dotnet/TelemetryPoc.slnx -c Debug --no-build`
Expected: `0/0`, **170 tests pass** (166 + 4 arch).
Then launch once (`dotnet run --project dotnet/src/TelemetryPoc.App -c Debug` with `RIDE_DB`/`RIDE_MBTILES`) and confirm the dashboard is unchanged.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "test(dotnet): NetArchTest onion boundary rules (Domain/Application/Infrastructure/Presentation)"
```

### Task 19: Refresh docs

**Files:**
- Modify: `dotnet/README.md`, `CLAUDE.md`

- [ ] **Step 1: Update the architecture description**

In `dotnet/README.md` and the `CLAUDE.md` "Layout"/"Architecture" sections, replace the `Core` + `App.Viz` description with the four rings (Domain / Application / Infrastructure / Presentation + App shell), DI/Host, and the NetArchTest boundary enforcement. Note the unit-test coverage now spans all rings.

- [ ] **Step 2: Commit**

```bash
git add -A && git commit -m "docs: describe the onion architecture + DI/Host in README + CLAUDE.md"
```

---

## Self-Review Notes (addressed)

- **Spec coverage:** Domain/Application/Infrastructure/Presentation/App (Tasks 1–13), ports (3), config/Options (12), DI/Host (13), logging (4,9,13), async load + disposal + error state (9,14), NetArchTest (18), test redistribution (15–17), docs (19). All spec sections map to tasks.
- **MvtBasemap placement** moved Domain→Infrastructure vs the spec draft (it needs `Mapbox.VectorTile`); this keeps Domain dependency-free and is noted in the File Structure section. Update the spec's Domain bullet to drop `MvtBasemap` and add it under Infrastructure when reviewing.
- **Type consistency:** `ITileSource.Decoded(int,int,int)` matches the old `MbTilesReader.Decoded`; `BasemapRenderer.Render(SKCanvas, Region, ITileSource)` updated at both definition (Task 7) and call site (Task 10); `SqliteRideSource` loaders chosen `public static` (Tasks 5 + 16 reconciled); `RideEngine(data, store, metrics)` matches the existing ctor.
- **Behavior parity:** the `RideEngine.Advance` gate, `Seek`, metrics-per-second, 30 Hz timer, and 10 Hz render cadence are preserved verbatim; only their host wiring and the time source (`ISystemClock`) change.
