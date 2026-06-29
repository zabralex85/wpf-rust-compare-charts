# .NET MVT Skia Map — Phase 1 (TelemetryPoc.Map Foundation) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the `TelemetryPoc.Map` class library with the pure math/data foundation of the map renderer — the camera `Region`, Web-Mercator projection, tile math (visible tiles + fit-to-bbox), and the dark INU style table — all xUnit-tested, no drawing yet.

**Architecture:** A new net8.0 class library `TelemetryPoc.Map` holds the renderer's pure logic so it is unit-tested without WPF or SkiaSharp. Phase 1 adds only math (`WebMercator`, `TileMath`) and data (`Region`, `MapStyle`); later phases add the MBTiles reader, MVT decode, Skia drawing, and the WPF host.

**Tech Stack:** .NET 8 class library (no external packages this phase), xUnit (in the existing `TelemetryPoc.Core.Tests`).

## Global Constraints

- New project `TelemetryPoc.Map` targets `net8.0`, `Nullable=enable`, `ImplicitUsings=enable`, **no external package references** this phase (SkiaSharp / Mapbox.VectorTile / Microsoft.Data.Sqlite come in Phase 2–3).
- `TelemetryPoc.Core` stays unchanged; tests live in `TelemetryPoc.Core.Tests`.
- Slippy-map convention: tile size **256 px**; `MapSize(z) = 256 * 2^z`. Web-Mercator with latitude clamped to `±85.05112878°`.
- `FitBbox` zoom is clamped to **[9, 14]**.
- Style mirrors `rust/src/ui/app/widgets/mapStyle.ts`: background `#0a0e14`; geometry layers in draw order — water (fill `#16384f`), landcover (fill `#0c1118`), landuse (fill `#111820`), transportation-casing (line `#0a0e14`), transportation (line `#5b6470`), building (fill `#232d38`). (Labels are Phase 3.)

## File Structure

- `dotnet/src/TelemetryPoc.Map/TelemetryPoc.Map.csproj` (new)
- `dotnet/src/TelemetryPoc.Map/Region.cs` (new) — camera record.
- `dotnet/src/TelemetryPoc.Map/WebMercator.cs` (new) — projection + tile coords.
- `dotnet/src/TelemetryPoc.Map/TileMath.cs` (new) — `TileRef`, `VisibleTiles`, `FitBbox`.
- `dotnet/src/TelemetryPoc.Map/MapStyle.cs` (new) — `PaintKind`, `StyleLayer`, `Layers`, `BackgroundHex`.
- `dotnet/TelemetryPoc.slnx` (modify) — register the project.
- `dotnet/src/TelemetryPoc.App/TelemetryPoc.App.csproj` (modify) — reference Map.
- `dotnet/tests/TelemetryPoc.Core.Tests/TelemetryPoc.Core.Tests.csproj` (modify) — reference Map.
- `dotnet/tests/TelemetryPoc.Core.Tests/WebMercatorTests.cs`, `TileMathTests.cs`, `MapStyleTests.cs` (new).

---

### Task 1: Project + Region + WebMercator (pure, xUnit)

**Files:**
- Create: `TelemetryPoc.Map.csproj`, `Region.cs`, `WebMercator.cs`, `WebMercatorTests.cs`
- Modify: `dotnet/TelemetryPoc.slnx`, `TelemetryPoc.App.csproj`, `TelemetryPoc.Core.Tests.csproj`

**Interfaces:**
- Produces:
  - `record Region(double CenterLat, double CenterLon, int Zoom, double Width, double Height)`
  - `WebMercator.MapSize(int z) → double` (`256 * 2^z`)
  - `WebMercator.LonLatToWorld(double lon, double lat, int z) → (double X, double Y)` (pixel in the world plane at zoom `z`)
  - `WebMercator.TileXY(double lon, double lat, int z) → (int X, int Y)` (`floor(world/256)`)

- [ ] **Step 1: Create the classlib csproj** — `dotnet/src/TelemetryPoc.Map/TelemetryPoc.Map.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Register in `dotnet/TelemetryPoc.slnx`** under `/src/` (sibling of the other src projects):

```xml
<Project Path="src/TelemetryPoc.Map/TelemetryPoc.Map.csproj" />
```

- [ ] **Step 3: Reference Map from App + the test project** — add to the ProjectReference `<ItemGroup>` of `dotnet/src/TelemetryPoc.App/TelemetryPoc.App.csproj`:

```xml
<ProjectReference Include="..\TelemetryPoc.Map\TelemetryPoc.Map.csproj" />
```

and to `dotnet/tests/TelemetryPoc.Core.Tests/TelemetryPoc.Core.Tests.csproj`:

```xml
<ProjectReference Include="..\..\src\TelemetryPoc.Map\TelemetryPoc.Map.csproj" />
```

- [ ] **Step 4: Write the failing test** — `dotnet/tests/TelemetryPoc.Core.Tests/WebMercatorTests.cs`:

```csharp
using TelemetryPoc.Map;
using Xunit;

public class WebMercatorTests
{
    [Fact]
    public void MapSize_is_256_times_2_pow_z()
    {
        Assert.Equal(256.0, WebMercator.MapSize(0));
        Assert.Equal(512.0, WebMercator.MapSize(1));
        Assert.Equal(256.0 * 4096, WebMercator.MapSize(12));
    }

    [Fact]
    public void LonLat_origin_is_map_center()
    {
        var (x, y) = WebMercator.LonLatToWorld(0, 0, 0);
        Assert.Equal(128.0, x, 6);
        Assert.Equal(128.0, y, 6);
    }

    [Theory]
    [InlineData(-180.0, 0.0)]   // left edge
    [InlineData(180.0, 256.0)]  // right edge
    public void LonLat_x_spans_the_world(double lon, double expectedX)
    {
        var (x, _) = WebMercator.LonLatToWorld(lon, 0, 0);
        Assert.Equal(expectedX, x, 6);
    }

    [Fact]
    public void LonLat_north_is_smaller_y()
    {
        // max-mercator latitude → y ≈ 0 (top); south → y ≈ 256 (bottom)
        var (_, yNorth) = WebMercator.LonLatToWorld(0, 85.05112878, 0);
        var (_, ySouth) = WebMercator.LonLatToWorld(0, -85.05112878, 0);
        Assert.Equal(0.0, yNorth, 3);
        Assert.Equal(256.0, ySouth, 3);
    }

    [Fact]
    public void TileXY_floors_world_over_256()
    {
        // z1: MapSize 512, origin world (256,256) → tile (1,1)
        Assert.Equal((1, 1), WebMercator.TileXY(0, 0, 1));
        // top-left corner
        Assert.Equal((0, 0), WebMercator.TileXY(-180, 85.05112878, 1));
    }
}
```

- [ ] **Step 5: Run → fail** — (from `dotnet/`) `dotnet test --filter WebMercatorTests`
Expected: compile error — types not defined.

- [ ] **Step 6: Implement `Region.cs`**:

```csharp
namespace TelemetryPoc.Map;

public sealed record Region(double CenterLat, double CenterLon, int Zoom, double Width, double Height);
```

- [ ] **Step 7: Implement `WebMercator.cs`**:

```csharp
namespace TelemetryPoc.Map;

public static class WebMercator
{
    private const double TileSize = 256.0;
    private const double MaxLat = 85.05112878;

    public static double MapSize(int z) => TileSize * Math.Pow(2, z);

    public static (double X, double Y) LonLatToWorld(double lon, double lat, int z)
    {
        var size = MapSize(z);
        var clampedLat = Math.Max(-MaxLat, Math.Min(MaxLat, lat));
        var x = (lon + 180.0) / 360.0 * size;
        var sin = Math.Sin(clampedLat * Math.PI / 180.0);
        var y = (0.5 - Math.Log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * size;
        return (x, y);
    }

    public static (int X, int Y) TileXY(double lon, double lat, int z)
    {
        var (x, y) = LonLatToWorld(lon, lat, z);
        return ((int)Math.Floor(x / TileSize), (int)Math.Floor(y / TileSize));
    }
}
```

- [ ] **Step 8: Run → pass** — `dotnet test --filter WebMercatorTests`, then full `dotnet test`.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(dotnet): TelemetryPoc.Map foundation — Region + WebMercator (xUnit)"
```

---

### Task 2: TileMath — visible tiles + fit-to-bbox (pure, xUnit)

**Files:**
- Create: `TileMath.cs`, `TileMathTests.cs`

**Interfaces:**
- Consumes: `WebMercator`, `Region`.
- Produces:
  - `record TileRef(int Z, int X, int Y, double ScreenX, double ScreenY)` (`ScreenX/Y` = the tile's top-left pixel on the canvas)
  - `TileMath.VisibleTiles(Region r) → IReadOnlyList<TileRef>` (tiles covering the viewport; out-of-range tile indices `<0` or `≥2^z` dropped)
  - `TileMath.FitBbox(double minLat, double minLon, double maxLat, double maxLon, double width, double height) → (double CenterLat, double CenterLon, int Zoom)` (center = bbox midpoint; largest `z∈[9,14]` whose bbox span fits `width×height` with 10% padding; else `9`)

- [ ] **Step 1: Write the failing test** — `dotnet/tests/TelemetryPoc.Core.Tests/TileMathTests.cs`:

```csharp
using System.Linq;
using TelemetryPoc.Map;
using Xunit;

public class TileMathTests
{
    [Fact]
    public void VisibleTiles_z0_single_tile_at_origin()
    {
        var tiles = TileMath.VisibleTiles(new Region(0, 0, 0, 256, 256));
        Assert.Single(tiles);
        var t = tiles[0];
        Assert.Equal((0, 0, 0), (t.Z, t.X, t.Y));
        Assert.Equal(0.0, t.ScreenX, 6);
        Assert.Equal(0.0, t.ScreenY, 6);
    }

    [Fact]
    public void VisibleTiles_z1_four_tiles_with_offsets()
    {
        var tiles = TileMath.VisibleTiles(new Region(0, 0, 1, 512, 512))
            .OrderBy(t => t.Y).ThenBy(t => t.X).ToList();
        Assert.Equal(4, tiles.Count);
        Assert.Equal((0, 0, 0.0, 0.0), (tiles[0].X, tiles[0].Y, tiles[0].ScreenX, tiles[0].ScreenY));
        Assert.Equal((1, 0, 256.0, 0.0), (tiles[1].X, tiles[1].Y, tiles[1].ScreenX, tiles[1].ScreenY));
        Assert.Equal((0, 1, 0.0, 256.0), (tiles[2].X, tiles[2].Y, tiles[2].ScreenX, tiles[2].ScreenY));
        Assert.Equal((1, 1, 256.0, 256.0), (tiles[3].X, tiles[3].Y, tiles[3].ScreenX, tiles[3].ScreenY));
    }

    [Fact]
    public void FitBbox_center_is_midpoint()
    {
        var (lat, lon, _) = TileMath.FitBbox(32.0, 34.7, 32.1, 34.9, 400, 400);
        Assert.Equal(32.05, lat, 6);
        Assert.Equal(34.8, lon, 6);
    }

    [Fact]
    public void FitBbox_tiny_bbox_picks_max_zoom()
    {
        // ~0.001° span fits at the highest allowed zoom
        var (_, _, z) = TileMath.FitBbox(32.080, 34.780, 32.081, 34.781, 400, 400);
        Assert.Equal(14, z);
    }

    [Fact]
    public void FitBbox_huge_bbox_clamps_to_min_zoom()
    {
        var (_, _, z) = TileMath.FitBbox(-40, -100, 60, 100, 400, 400);
        Assert.Equal(9, z);
    }
}
```

- [ ] **Step 2: Run → fail** — `dotnet test --filter TileMathTests`

- [ ] **Step 3: Implement `TileMath.cs`**:

```csharp
namespace TelemetryPoc.Map;

public sealed record TileRef(int Z, int X, int Y, double ScreenX, double ScreenY);

public static class TileMath
{
    private const double TileSize = 256.0;

    public static IReadOnlyList<TileRef> VisibleTiles(Region r)
    {
        var (cx, cy) = WebMercator.LonLatToWorld(r.CenterLon, r.CenterLat, r.Zoom);
        var left = cx - r.Width / 2;
        var top = cy - r.Height / 2;
        var maxIndex = (1 << r.Zoom) - 1;

        var x0 = (int)Math.Floor(left / TileSize);
        var x1 = (int)Math.Floor((left + r.Width) / TileSize);
        var y0 = (int)Math.Floor(top / TileSize);
        var y1 = (int)Math.Floor((top + r.Height) / TileSize);

        var tiles = new List<TileRef>();
        for (int ty = y0; ty <= y1; ty++)
        {
            if (ty < 0 || ty > maxIndex) continue;
            for (int tx = x0; tx <= x1; tx++)
            {
                if (tx < 0 || tx > maxIndex) continue;
                tiles.Add(new TileRef(r.Zoom, tx, ty, tx * TileSize - left, ty * TileSize - top));
            }
        }
        return tiles;
    }

    public static (double CenterLat, double CenterLon, int Zoom) FitBbox(
        double minLat, double minLon, double maxLat, double maxLon, double width, double height)
    {
        var centerLat = (minLat + maxLat) / 2;
        var centerLon = (minLon + maxLon) / 2;
        for (int z = 14; z >= 9; z--)
        {
            var (x0, y0) = WebMercator.LonLatToWorld(minLon, maxLat, z); // NW
            var (x1, y1) = WebMercator.LonLatToWorld(maxLon, minLat, z); // SE
            var dx = Math.Abs(x1 - x0);
            var dy = Math.Abs(y1 - y0);
            if (dx <= width * 0.9 && dy <= height * 0.9) return (centerLat, centerLon, z);
        }
        return (centerLat, centerLon, 9);
    }
}
```

- [ ] **Step 4: Run → pass** — `dotnet test --filter TileMathTests`, then full `dotnet test`.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(dotnet): TelemetryPoc.Map TileMath — visible tiles + fit-to-bbox (xUnit)"
```

---

### Task 3: MapStyle — dark INU layer table (pure data, xUnit)

**Files:**
- Create: `MapStyle.cs`, `MapStyleTests.cs`

**Interfaces:**
- Produces:
  - `enum PaintKind { Fill, Line }`
  - `record StyleLayer(string Id, string SourceLayer, PaintKind Kind, string ColorHex, double Width)`
  - `MapStyle.BackgroundHex → string` (`"#0a0e14"`)
  - `MapStyle.Layers → IReadOnlyList<StyleLayer>` (geometry layers in draw order)

- [ ] **Step 1: Write the failing test** — `dotnet/tests/TelemetryPoc.Core.Tests/MapStyleTests.cs`:

```csharp
using System.Linq;
using TelemetryPoc.Map;
using Xunit;

public class MapStyleTests
{
    [Fact]
    public void Background_is_inu_dark()
        => Assert.Equal("#0a0e14", MapStyle.BackgroundHex);

    [Fact]
    public void Layers_are_in_draw_order_with_inu_colors()
    {
        var ids = MapStyle.Layers.Select(l => l.Id).ToArray();
        Assert.Equal(
            new[] { "water", "landcover", "landuse", "transportation-casing", "transportation", "building" },
            ids);
    }

    [Fact]
    public void Water_is_a_fill_with_the_inu_color()
    {
        var water = MapStyle.Layers.First(l => l.Id == "water");
        Assert.Equal(PaintKind.Fill, water.Kind);
        Assert.Equal("water", water.SourceLayer);
        Assert.Equal("#16384f", water.ColorHex);
    }

    [Fact]
    public void Transportation_layers_share_source_but_differ()
    {
        var casing = MapStyle.Layers.First(l => l.Id == "transportation-casing");
        var road = MapStyle.Layers.First(l => l.Id == "transportation");
        Assert.Equal("transportation", casing.SourceLayer);
        Assert.Equal("transportation", road.SourceLayer);
        Assert.Equal(PaintKind.Line, casing.Kind);
        Assert.Equal("#0a0e14", casing.ColorHex);
        Assert.Equal("#5b6470", road.ColorHex);
        Assert.True(casing.Width > road.Width); // casing is wider
    }
}
```

- [ ] **Step 2: Run → fail** — `dotnet test --filter MapStyleTests`

- [ ] **Step 3: Implement `MapStyle.cs`**:

```csharp
namespace TelemetryPoc.Map;

public enum PaintKind { Fill, Line }

public sealed record StyleLayer(string Id, string SourceLayer, PaintKind Kind, string ColorHex, double Width);

public static class MapStyle
{
    public const string BackgroundHex = "#0a0e14";

    public static IReadOnlyList<StyleLayer> Layers { get; } = new[]
    {
        new StyleLayer("water", "water", PaintKind.Fill, "#16384f", 0),
        new StyleLayer("landcover", "landcover", PaintKind.Fill, "#0c1118", 0),
        new StyleLayer("landuse", "landuse", PaintKind.Fill, "#111820", 0),
        new StyleLayer("transportation-casing", "transportation", PaintKind.Line, "#0a0e14", 3.0),
        new StyleLayer("transportation", "transportation", PaintKind.Line, "#5b6470", 1.2),
        new StyleLayer("building", "building", PaintKind.Fill, "#232d38", 0),
    };
}
```

- [ ] **Step 4: Run → pass** — `dotnet test --filter MapStyleTests`, then full `dotnet test`.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(dotnet): TelemetryPoc.Map MapStyle — dark INU layer table (xUnit)"
```

---

## Self-Review

**Spec coverage (Phase 1):** `TelemetryPoc.Map` project + slnx/App/test refs (Task 1); `Region` + `WebMercator` projection/tile coords (Task 1); `TileMath` VisibleTiles + FitBbox clamped [9,14] (Task 2); `MapStyle` geometry layer table mirroring rust mapStyle (Task 3). All xUnit-tested. No drawing/SkiaSharp/MBTiles this phase (Phase 2–3). ✓

**Placeholder scan:** No TBD/TODO. Every step carries complete code + exact test values. Labels deferred to Phase 3 (explicit), not a gap. ✓

**Type consistency:** `Region(CenterLat,CenterLon,Zoom,Width,Height)` used by `TileMath.VisibleTiles`; `WebMercator.LonLatToWorld/MapSize/TileXY` consumed by `TileMath`; `TileRef(Z,X,Y,ScreenX,ScreenY)`, `FitBbox` return tuple, `StyleLayer(Id,SourceLayer,Kind,ColorHex,Width)`/`PaintKind`/`MapStyle.Layers`/`BackgroundHex` — consistent across Tasks 1→3. Test values verified: `MapSize(1)=512`, `TileXY(0,0,1)=(1,1)`, z1 viewport → 4 tiles at (0/256) offsets, `FitBbox` midpoint 32.05/34.8. ✓
