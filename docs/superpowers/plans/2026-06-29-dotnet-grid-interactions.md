# .NET WPF Grid Interactions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the .NET dashboard's three fixed regions with one unified snap-to-grid surface where map, gauges, and charts are free-floating cells the user can create, move, resize, reorder, remove, and reconfigure — plus line-chart zoom/hover and interactive map pan/zoom — mirroring the Rust app's Phase 4.

**Architecture:** Pure grid/zoom/projection math lives in `TelemetryPoc.App.Viz` and `TelemetryPoc.Map` (xUnit-tested). A new `DashboardViewModel` holds an `ObservableCollection<WidgetViewModel>` seeded from channels and delegates every geometry decision to the pure layer. A new `WidgetGridView` (ItemsControl over a Canvas) renders cells; WPF implicit `DataTemplate`s map each inner VM to its existing view. Interaction is wired in code-behind (OLE drag for param→create, mouse-capture for move/resize, context menu for line zoom, mouse handlers for map pan/zoom).

**Tech Stack:** .NET 8, WPF/XAML, MVVM, ScottPlot.WPF 5, SkiaSharp, xUnit.

## Global Constraints

- **Grid geometry (mirror Rust exactly):** cell 158 DIP, gap 10, **pitch 168**; resize **deadzone 144** (`pitch − 24`).
- **Size constraints:** gauge 1–6 cols/rows **always square**; line 1–6 cols, 1–4 rows; map 2–8 cols, 2–6 rows.
- **Drag-create default:** gauge, **1×1**, at the snapped cell.
- **Toggle:** gauge→line `cols=max(2,cols), rows=max(1,rows)` (then clamped); line→gauge `1×1`.
- **Line zoom:** ×2 / ÷2, **clamp [1,8]**; visible window = `60000 / zoom` ms.
- **Seed:** map 4×4 if both `map_lat`+`map_lon` channels exist; gauge 1×1 per `widget=="gauge"` channel; line 2×1 per `widget=="strip"` channel; place each via first-fit in an 8-col virtual grid.
- **No collision detection** — widgets may overlap; no auto-reflow.
- **In-memory layout only** — no persistence; resets on relaunch.
- **Map zoom range** `[9, 14]` (the MBTiles zoom levels `TileMath.FitBbox` searches).
- **Test correctness in pure logic** (App.Viz / Map, xUnit). XAML/Skia/ScottPlot interaction is **build-verified + live-launch confirmed** against `docs/reference/dashboard-target.md` and the Rust app — never unit-tested. Keep the two stacks behaviorally equivalent.

## Commands

- Build: `cd dotnet && dotnet build TelemetryPoc.slnx`
- All tests: `cd dotnet && dotnet test`
- One test class: `cd dotnet && dotnet test --filter "FullyQualifiedName~WidgetLayoutTests"`
- Launch (manual visual check): `cd dotnet && dotnet run --project src/TelemetryPoc.App` (needs `data/ride.db` or `data/ride_small.db`)

## File Structure

**Create:**
- `dotnet/src/TelemetryPoc.App.Viz/WidgetLayout.cs` — `WidgetKind` enum, `Widget` record, grid/zoom math.
- `dotnet/src/TelemetryPoc.App.Viz/WidgetSeed.cs` — `SeedLayout(channels)`.
- `dotnet/src/TelemetryPoc.App.Viz/NearestSample.cs` — hover nearest-point search.
- `dotnet/src/TelemetryPoc.Map/MapInteract.cs` — `Pan`, `ZoomAt`.
- `dotnet/src/TelemetryPoc.App/ViewModels/WidgetViewModel.cs` — one grid cell's VM.
- `dotnet/src/TelemetryPoc.App/ViewModels/DashboardViewModel.cs` — the widget collection + ops.
- `dotnet/src/TelemetryPoc.App/Views/WidgetGridView.xaml` (+ `.xaml.cs`) — the grid surface + cell template + interaction handlers.
- Tests: `dotnet/tests/TelemetryPoc.Core.Tests/{WidgetLayoutTests,WidgetSeedTests,NearestSampleTests,MapInteractTests}.cs` and additions to `WebMercatorTests.cs`.

**Modify:**
- `dotnet/src/TelemetryPoc.Map/WebMercator.cs` — add `WorldToLonLat`.
- `dotnet/src/TelemetryPoc.Map/TileMath.cs` — add `MinZoom`/`MaxZoom` consts; use them in `FitBbox`.
- `dotnet/src/TelemetryPoc.App/ViewModels/MapWidgetViewModel.cs` — add `SetRegion`.
- `dotnet/src/TelemetryPoc.App/ViewModels/LineChartViewModel.cs` — `Zoom`, `ZoomBy`, `ResetZoom`, window from zoom.
- `dotnet/src/TelemetryPoc.App/ViewModels/OverviewViewModel.cs` — expose `Dashboard`; drop the old `Gauges`/`LineCharts`/`MapWidget`-region wiring (Groups stay).
- `dotnet/src/TelemetryPoc.App/Views/OverviewView.xaml` — replace the 3-region body with `WidgetGridView`.
- `dotnet/src/TelemetryPoc.App/Views/{GaugeView,LineChartView,MapWidgetView}.xaml` — remove fixed sizes + internal name title (cell header owns the title).
- `dotnet/src/TelemetryPoc.App/Views/{LineChartView,MapWidgetView}.xaml.cs` — line context-menu + hover; map pan/zoom handlers.
- `dotnet/src/TelemetryPoc.App/Views/ParamPanel.xaml(.cs)` — make rows OLE drag sources.

---

### Task 1: Widget grid math (pure)

**Files:**
- Create: `dotnet/src/TelemetryPoc.App.Viz/WidgetLayout.cs`
- Test: `dotnet/tests/TelemetryPoc.Core.Tests/WidgetLayoutTests.cs`

**Interfaces:**
- Produces: `enum WidgetKind { Gauge, Line, Map }`; `record Widget(string Id, WidgetKind Kind, int? ChannelId, string Name, string Unit, int Col, int Row, int Cols, int Rows, int Zoom)`; static `WidgetLayout` with `Pitch=168, CellSize=158, Gap=10, Deadzone=144, ZoomMin=1, ZoomMax=8`, `(int Col,int Row) CellFromPoint(double x,double y)`, `int ResizeStep(double deltaPx)`, `(int Cols,int Rows) ClampSize(WidgetKind,int,int)`, `(WidgetKind Kind,int Cols,int Rows) Toggle(WidgetKind,int,int)`, `int ZoomBy(int zoom,double factor)`.

- [ ] **Step 1: Write the failing test**

Create `dotnet/tests/TelemetryPoc.Core.Tests/WidgetLayoutTests.cs`:

```csharp
using TelemetryPoc.App.Viz;
using Xunit;

public class WidgetLayoutTests
{
    [Theory]
    [InlineData(0, 0, 1, 1)]       // top-left cell is (1,1)
    [InlineData(10, 10, 1, 1)]     // within first pitch
    [InlineData(168, 0, 2, 1)]     // one pitch right → col 2
    [InlineData(0, 336, 1, 3)]     // two pitches down → row 3
    public void CellFromPoint_is_one_indexed_and_pitch_snapped(double x, double y, int col, int row)
    {
        var c = WidgetLayout.CellFromPoint(x, y);
        Assert.Equal((col, row), c);
    }

    [Theory]
    [InlineData(0, 0)]      // no movement
    [InlineData(23, 0)]     // inside deadzone
    [InlineData(24, 1)]     // 24px past start = +1 cell (pitch-deadzone)
    [InlineData(192, 2)]    // 168+24
    [InlineData(-24, -1)]   // shrink one cell
    public void ResizeStep_uses_144_deadzone(double delta, int steps)
        => Assert.Equal(steps, WidgetLayout.ResizeStep(delta));

    [Fact]
    public void ClampSize_gauge_is_square_and_capped()
    {
        Assert.Equal((3, 3), WidgetLayout.ClampSize(WidgetKind.Gauge, 3, 2)); // square = max
        Assert.Equal((6, 6), WidgetLayout.ClampSize(WidgetKind.Gauge, 9, 1)); // cap 6
        Assert.Equal((1, 1), WidgetLayout.ClampSize(WidgetKind.Gauge, 0, 0)); // floor 1
    }

    [Fact]
    public void ClampSize_line_and_map_bounds()
    {
        Assert.Equal((6, 4), WidgetLayout.ClampSize(WidgetKind.Line, 9, 9)); // line 6x4 cap
        Assert.Equal((1, 1), WidgetLayout.ClampSize(WidgetKind.Line, 0, 0));
        Assert.Equal((2, 2), WidgetLayout.ClampSize(WidgetKind.Map, 1, 1)); // map min 2
        Assert.Equal((8, 6), WidgetLayout.ClampSize(WidgetKind.Map, 99, 99)); // map cap
    }

    [Fact]
    public void Toggle_gauge_to_line_then_back()
    {
        var (k1, c1, r1) = WidgetLayout.Toggle(WidgetKind.Gauge, 1, 1);
        Assert.Equal((WidgetKind.Line, 2, 1), (k1, c1, r1)); // min 2 cols
        var (k2, c2, r2) = WidgetLayout.Toggle(WidgetKind.Line, 4, 3);
        Assert.Equal((WidgetKind.Gauge, 1, 1), (k2, c2, r2)); // back to 1x1
    }

    [Theory]
    [InlineData(1, 2.0, 2)]
    [InlineData(4, 2.0, 8)]
    [InlineData(8, 2.0, 8)]   // clamp top
    [InlineData(2, 0.5, 1)]
    [InlineData(1, 0.5, 1)]   // clamp bottom
    public void ZoomBy_clamps_1_to_8(int z, double f, int expected)
        => Assert.Equal(expected, WidgetLayout.ZoomBy(z, f));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd dotnet && dotnet test --filter "FullyQualifiedName~WidgetLayoutTests"`
Expected: FAIL — `WidgetLayout`/`WidgetKind`/`Widget` do not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

Create `dotnet/src/TelemetryPoc.App.Viz/WidgetLayout.cs`:

```csharp
using System;

namespace TelemetryPoc.App.Viz;

public enum WidgetKind { Gauge, Line, Map }

public sealed record Widget(
    string Id, WidgetKind Kind, int? ChannelId,
    string Name, string Unit,
    int Col, int Row, int Cols, int Rows, int Zoom);

/// <summary>Pure grid geometry + widget sizing, ported from the Rust app's
/// widgetModel.ts + dropGrid.ts. 1-indexed columns/rows, no collision.</summary>
public static class WidgetLayout
{
    public const int Pitch = 168;
    public const int CellSize = 158;
    public const int Gap = 10;
    public const int Deadzone = 144; // Pitch - 24
    public const int ZoomMin = 1;
    public const int ZoomMax = 8;

    /// <summary>Canvas-relative point (already scroll-adjusted) → 1-indexed cell.</summary>
    public static (int Col, int Row) CellFromPoint(double x, double y)
        => (Math.Max(1, (int)Math.Floor(x / Pitch) + 1),
            Math.Max(1, (int)Math.Floor(y / Pitch) + 1));

    /// <summary>Pixel drag delta → grid-cell step with a 144px deadzone.</summary>
    public static int ResizeStep(double deltaPx)
        => deltaPx >= 0
            ? (int)Math.Floor((deltaPx + Deadzone) / Pitch)
            : (int)Math.Ceiling((deltaPx - Deadzone) / Pitch);

    public static (int Cols, int Rows) ClampSize(WidgetKind kind, int cols, int rows)
    {
        switch (kind)
        {
            case WidgetKind.Gauge:
                var s = Math.Clamp(Math.Max(cols, rows), 1, 6);
                return (s, s);
            case WidgetKind.Line:
                return (Math.Clamp(cols, 1, 6), Math.Clamp(rows, 1, 4));
            case WidgetKind.Map:
                return (Math.Clamp(cols, 2, 8), Math.Clamp(rows, 2, 6));
            default:
                return (cols, rows);
        }
    }

    public static (WidgetKind Kind, int Cols, int Rows) Toggle(WidgetKind kind, int cols, int rows)
    {
        if (kind == WidgetKind.Gauge)
        {
            var (c, r) = ClampSize(WidgetKind.Line, Math.Max(2, cols), Math.Max(1, rows));
            return (WidgetKind.Line, c, r);
        }
        if (kind == WidgetKind.Line)
        {
            var (c, r) = ClampSize(WidgetKind.Gauge, 1, 1);
            return (WidgetKind.Gauge, c, r);
        }
        return (kind, cols, rows); // map does not toggle
    }

    public static int ZoomBy(int zoom, double factor)
        => (int)Math.Clamp(Math.Round(zoom * factor), ZoomMin, ZoomMax);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd dotnet && dotnet test --filter "FullyQualifiedName~WidgetLayoutTests"`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/TelemetryPoc.App.Viz/WidgetLayout.cs dotnet/tests/TelemetryPoc.Core.Tests/WidgetLayoutTests.cs
git commit -m "feat(dotnet): widget grid geometry + sizing math"
```

---

### Task 2: Seed layout from channels (pure)

**Files:**
- Create: `dotnet/src/TelemetryPoc.App.Viz/WidgetSeed.cs`
- Test: `dotnet/tests/TelemetryPoc.Core.Tests/WidgetSeedTests.cs`

**Interfaces:**
- Consumes: `Widget`, `WidgetKind` (Task 1); `ChannelMeta` (Core: has `int Id`, `string Name`, `string Unit`, `string Widget`).
- Produces: static `WidgetSeed.SeedLayout(IReadOnlyList<ChannelMeta> channels) → IReadOnlyList<Widget>`; static `WidgetLayout.FirstFit` is added here too: `(int Col,int Row) FirstFit(IReadOnlyList<Widget> placed, int cols, int rows, int seedCols = 8)`.

> Channels arrive already ordered by `display_order` (the DB reader returns them so; `OverviewViewModel.BuildGroups` relies on this). Iterate in given order — do not re-sort.

- [ ] **Step 1: Write the failing test**

Create `dotnet/tests/TelemetryPoc.Core.Tests/WidgetSeedTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using TelemetryPoc.App.Viz;
using TelemetryPoc.Core;
using Xunit;

public class WidgetSeedTests
{
    private static ChannelMeta Ch(int id, string widget, string name = "n", string unit = "u")
        => new ChannelMeta(id, name, unit, widget, 0, 100);

    [Fact]
    public void Seeds_map_gauges_and_lines_with_expected_sizes()
    {
        var channels = new List<ChannelMeta>
        {
            Ch(1, "map_lat"), Ch(2, "map_lon"),
            Ch(3, "gauge"), Ch(4, "gauge"),
            Ch(5, "strip"),
            Ch(6, "table"), // ignored
        };
        var w = WidgetSeed.SeedLayout(channels);

        var map = Assert.Single(w.Where(x => x.Kind == WidgetKind.Map));
        Assert.Equal((4, 4), (map.Cols, map.Rows));
        Assert.Equal("map", map.Id);

        Assert.Equal(2, w.Count(x => x.Kind == WidgetKind.Gauge));
        Assert.All(w.Where(x => x.Kind == WidgetKind.Gauge), g => Assert.Equal((1, 1), (g.Cols, g.Rows)));
        Assert.Contains(w, x => x.Id == "gauge-3");

        var line = Assert.Single(w.Where(x => x.Kind == WidgetKind.Line));
        Assert.Equal((2, 1), (line.Cols, line.Rows));
        Assert.Equal("line-5", line.Id);
        Assert.Equal(5, line.ChannelId);
    }

    [Fact]
    public void No_map_when_lat_or_lon_missing()
    {
        var w = WidgetSeed.SeedLayout(new List<ChannelMeta> { Ch(1, "map_lat"), Ch(2, "gauge") });
        Assert.DoesNotContain(w, x => x.Kind == WidgetKind.Map);
    }

    [Fact]
    public void FirstFit_packs_row_major_without_overlap()
    {
        var placed = new List<Widget>();
        var a = WidgetLayout.FirstFit(placed, 4, 4);     // (1,1)
        placed.Add(new Widget("a", WidgetKind.Map, null, "", "", a.Col, a.Row, 4, 4, 1));
        var b = WidgetLayout.FirstFit(placed, 1, 1);     // first free after the 4x4 block
        Assert.Equal((1, 1), a);
        Assert.Equal((5, 1), b); // cols 1-4 taken on row 1, next free col is 5
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd dotnet && dotnet test --filter "FullyQualifiedName~WidgetSeedTests"`
Expected: FAIL — `WidgetSeed` and `WidgetLayout.FirstFit` do not exist.

> If `ChannelMeta`'s constructor signature differs from `(id, name, unit, widget, min, max)`, adjust the `Ch` helper to match the real record — check `dotnet/src/TelemetryPoc.Core/ChannelMeta.cs`. Keep the test's intent identical.

- [ ] **Step 3: Write minimal implementation**

Add `FirstFit` to `WidgetLayout` (in `WidgetLayout.cs`, inside the class):

```csharp
    /// <summary>Row-major first-fit packing in a virtual grid `seedCols` wide.
    /// Returns the 1-indexed top-left cell for a cols×rows block that does not
    /// overlap any already-placed widget. No row cap (grid grows downward).</summary>
    public static (int Col, int Row) FirstFit(
        System.Collections.Generic.IReadOnlyList<Widget> placed, int cols, int rows, int seedCols = 8)
    {
        for (int row = 1; ; row++)
            for (int col = 1; col + cols - 1 <= seedCols; col++)
            {
                bool free = true;
                foreach (var p in placed)
                {
                    bool overlap = col < p.Col + p.Cols && p.Col < col + cols
                                && row < p.Row + p.Rows && p.Row < row + rows;
                    if (overlap) { free = false; break; }
                }
                if (free) return (col, row);
            }
    }
```

Create `dotnet/src/TelemetryPoc.App.Viz/WidgetSeed.cs`:

```csharp
using System.Collections.Generic;
using TelemetryPoc.Core;

namespace TelemetryPoc.App.Viz;

/// <summary>Builds the initial widget layout from channel metadata, mirroring the
/// Rust app's layout.ts: a 4×4 map (if lat+lon exist), a 1×1 gauge per gauge
/// channel, a 2×1 line per strip channel — each placed via first-fit.</summary>
public static class WidgetSeed
{
    public static IReadOnlyList<Widget> SeedLayout(IReadOnlyList<ChannelMeta> channels)
    {
        var placed = new List<Widget>();

        bool hasLat = false, hasLon = false;
        foreach (var ch in channels)
        {
            if (ch.Widget == "map_lat") hasLat = true;
            if (ch.Widget == "map_lon") hasLon = true;
        }
        if (hasLat && hasLon)
        {
            var (c, r) = WidgetLayout.FirstFit(placed, 4, 4);
            placed.Add(new Widget("map", WidgetKind.Map, null, "FLIGHT TRACK", "", c, r, 4, 4, 1));
        }

        foreach (var ch in channels)
            if (ch.Widget == "gauge")
            {
                var (c, r) = WidgetLayout.FirstFit(placed, 1, 1);
                placed.Add(new Widget($"gauge-{ch.Id}", WidgetKind.Gauge, ch.Id, ch.Name, ch.Unit, c, r, 1, 1, 1));
            }

        foreach (var ch in channels)
            if (ch.Widget == "strip")
            {
                var (c, r) = WidgetLayout.FirstFit(placed, 2, 1);
                placed.Add(new Widget($"line-{ch.Id}", WidgetKind.Line, ch.Id, ch.Name, ch.Unit, c, r, 2, 1, 1));
            }

        return placed;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd dotnet && dotnet test --filter "FullyQualifiedName~WidgetSeedTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/TelemetryPoc.App.Viz/WidgetSeed.cs dotnet/src/TelemetryPoc.App.Viz/WidgetLayout.cs dotnet/tests/TelemetryPoc.Core.Tests/WidgetSeedTests.cs
git commit -m "feat(dotnet): seed widget layout from channels"
```

---

### Task 3: Map pan/zoom math (pure)

**Files:**
- Modify: `dotnet/src/TelemetryPoc.Map/WebMercator.cs` (add `WorldToLonLat`)
- Modify: `dotnet/src/TelemetryPoc.Map/TileMath.cs` (add `MinZoom`/`MaxZoom`)
- Create: `dotnet/src/TelemetryPoc.Map/MapInteract.cs`
- Test: `dotnet/tests/TelemetryPoc.Core.Tests/WebMercatorTests.cs` (append), `dotnet/tests/TelemetryPoc.Core.Tests/MapInteractTests.cs`

**Interfaces:**
- Consumes: `Region` (`CenterLat,CenterLon,Zoom,Width,Height`), `WebMercator.LonLatToWorld`, `WebMercator.MapSize`, `MapProject.GpsToScreen`.
- Produces: `WebMercator.WorldToLonLat(double worldX,double worldY,int z) → (double Lon,double Lat)`; `TileMath.MinZoom=9`, `TileMath.MaxZoom=14`; `MapInteract.Pan(Region r,double dxPx,double dyPx) → Region`; `MapInteract.ZoomAt(Region r,double cursorX,double cursorY,int step,int minZoom,int maxZoom) → Region`.

- [ ] **Step 1: Write the failing tests**

Append to `dotnet/tests/TelemetryPoc.Core.Tests/WebMercatorTests.cs` (inside the existing test class):

```csharp
    [Theory]
    [InlineData(34.78, 32.08, 12)]
    [InlineData(-0.13, 51.50, 9)]
    [InlineData(139.69, 35.69, 14)]
    public void WorldToLonLat_round_trips_LonLatToWorld(double lon, double lat, int z)
    {
        var (wx, wy) = WebMercator.LonLatToWorld(lon, lat, z);
        var (lon2, lat2) = WebMercator.WorldToLonLat(wx, wy, z);
        Assert.Equal(lon, lon2, 4);
        Assert.Equal(lat, lat2, 4);
    }
```

Create `dotnet/tests/TelemetryPoc.Core.Tests/MapInteractTests.cs`:

```csharp
using TelemetryPoc.Map;
using Xunit;

public class MapInteractTests
{
    private static Region R() => new Region(32.08, 34.78, 12, 400, 300);

    [Fact]
    public void Pan_right_moves_center_west()
    {
        var r = R();
        var p = MapInteract.Pan(r, 50, 0); // drag content right by 50px
        Assert.True(p.CenterLon < r.CenterLon); // viewport shifted, center longitude decreases
        Assert.Equal(r.CenterLat, p.CenterLat, 6);
        Assert.Equal(r.Zoom, p.Zoom);
    }

    [Fact]
    public void Pan_down_moves_center_north()
    {
        var p = MapInteract.Pan(R(), 0, 40);
        Assert.True(p.CenterLat > R().CenterLat); // dragging down reveals north
    }

    [Fact]
    public void ZoomAt_increments_zoom_and_clamps()
    {
        var r = R();
        var z13 = MapInteract.ZoomAt(r, 200, 150, +1, 9, 14);
        Assert.Equal(13, z13.Zoom);
        var capped = MapInteract.ZoomAt(r with { Zoom = 14 }, 200, 150, +1, 9, 14);
        Assert.Equal(14, capped.Zoom); // no change at max
        var floored = MapInteract.ZoomAt(r with { Zoom = 9 }, 200, 150, -1, 9, 14);
        Assert.Equal(9, floored.Zoom);
    }

    [Fact]
    public void ZoomAt_keeps_cursor_geopoint_under_cursor()
    {
        var r = R();
        double cx = 120, cy = 90;
        var (curLon, curLat) = WebMercator.WorldToLonLat(
            WebMercator.LonLatToWorld(r.CenterLon, r.CenterLat, r.Zoom).X - r.Width / 2 + cx,
            WebMercator.LonLatToWorld(r.CenterLon, r.CenterLat, r.Zoom).Y - r.Height / 2 + cy,
            r.Zoom);
        var z = MapInteract.ZoomAt(r, cx, cy, +1, 9, 14);
        var (sx, sy) = MapProject.GpsToScreen(z, curLat, curLon);
        Assert.Equal(cx, sx, 1); // same screen pixel after zoom
        Assert.Equal(cy, sy, 1);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd dotnet && dotnet test --filter "FullyQualifiedName~MapInteractTests|FullyQualifiedName~WebMercatorTests"`
Expected: FAIL — `WorldToLonLat`, `MapInteract` missing.

- [ ] **Step 3: Write minimal implementation**

Add to `dotnet/src/TelemetryPoc.Map/WebMercator.cs` (inside the class):

```csharp
    public static (double Lon, double Lat) WorldToLonLat(double worldX, double worldY, int z)
    {
        var size = MapSize(z);
        var lon = worldX / size * 360.0 - 180.0;
        var yNorm = worldY / size;
        var lat = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * yNorm))) * 180.0 / Math.PI;
        return (lon, lat);
    }
```

Add to `dotnet/src/TelemetryPoc.Map/TileMath.cs` (top of the `TileMath` class) and use the consts in `FitBbox`:

```csharp
    public const int MinZoom = 9;
    public const int MaxZoom = 14;
```

Change the `FitBbox` loop header from `for (int z = 14; z >= 9; z--)` to `for (int z = MaxZoom; z >= MinZoom; z--)` and the fallback `return (centerLat, centerLon, 9);` to `return (centerLat, centerLon, MinZoom);`.

Create `dotnet/src/TelemetryPoc.Map/MapInteract.cs`:

```csharp
namespace TelemetryPoc.Map;

/// <summary>Pure interaction math for the Skia map: drag-pan and zoom-toward-cursor.
/// All geometry goes through Web Mercator world coordinates at the relevant zoom.</summary>
public static class MapInteract
{
    /// <summary>Shift the viewport by a screen-pixel delta. Dragging content by
    /// (dxPx,dyPx) moves the center by the opposite world delta.</summary>
    public static Region Pan(Region r, double dxPx, double dyPx)
    {
        var (cx, cy) = WebMercator.LonLatToWorld(r.CenterLon, r.CenterLat, r.Zoom);
        var (lon, lat) = WebMercator.WorldToLonLat(cx - dxPx, cy - dyPx, r.Zoom);
        return r with { CenterLat = lat, CenterLon = lon };
    }

    /// <summary>Zoom by integer `step` (clamped to [minZoom,maxZoom]) while keeping the
    /// geographic point under the cursor fixed on screen (MapLibre-style).</summary>
    public static Region ZoomAt(Region r, double cursorX, double cursorY, int step, int minZoom, int maxZoom)
    {
        var newZoom = System.Math.Clamp(r.Zoom + step, minZoom, maxZoom);
        if (newZoom == r.Zoom) return r;

        // Geo point currently under the cursor.
        var (cx, cy) = WebMercator.LonLatToWorld(r.CenterLon, r.CenterLat, r.Zoom);
        var cursorWorldX = cx - r.Width / 2 + cursorX;
        var cursorWorldY = cy - r.Height / 2 + cursorY;
        var (geoLon, geoLat) = WebMercator.WorldToLonLat(cursorWorldX, cursorWorldY, r.Zoom);

        // Place that geo point back under the cursor at the new zoom.
        var (gx, gy) = WebMercator.LonLatToWorld(geoLon, geoLat, newZoom);
        var newCenterX = gx - cursorX + r.Width / 2;
        var newCenterY = gy - cursorY + r.Height / 2;
        var (newLon, newLat) = WebMercator.WorldToLonLat(newCenterX, newCenterY, newZoom);
        return r with { CenterLat = newLat, CenterLon = newLon, Zoom = newZoom };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd dotnet && dotnet test --filter "FullyQualifiedName~MapInteractTests|FullyQualifiedName~WebMercatorTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/TelemetryPoc.Map/WebMercator.cs dotnet/src/TelemetryPoc.Map/TileMath.cs dotnet/src/TelemetryPoc.Map/MapInteract.cs dotnet/tests/TelemetryPoc.Core.Tests/WebMercatorTests.cs dotnet/tests/TelemetryPoc.Core.Tests/MapInteractTests.cs
git commit -m "feat(dotnet): map pan/zoom math (inverse mercator + MapInteract)"
```

---

### Task 4: Dashboard + widget view-models (state)

**Files:**
- Create: `dotnet/src/TelemetryPoc.App/ViewModels/WidgetViewModel.cs`
- Create: `dotnet/src/TelemetryPoc.App/ViewModels/DashboardViewModel.cs`
- Modify: `dotnet/src/TelemetryPoc.App/ViewModels/MapWidgetViewModel.cs` (add `SetRegion`)

**Interfaces:**
- Consumes: `Widget`, `WidgetKind`, `WidgetLayout`, `WidgetSeed` (Tasks 1–2); `RideSession`, `TelemetryStore`, `ChannelMeta` (Core); existing `GaugeViewModel`, `LineChartViewModel`, `MapWidgetViewModel`.
- Produces: `WidgetViewModel` (props `Id`, `Kind`, `object Content`, `int Col/Row/Cols/Rows`, computed `double Left/Top/Width/Height`, `bool IsToggleable`); `DashboardViewModel` (`ObservableCollection<WidgetViewModel> Widgets`, methods `AddGauge(int channelId,int col,int row)`, `Move(string id,int col,int row)`, `Resize(string id,int cols,int rows)`, `Toggle(string id)`, `Remove(string id)`, `ZoomBy(string id,double factor)`); `MapWidgetViewModel.SetRegion(Region)`.

This task wires the state but does **not** change any view yet — the new VMs compile and are unit-buildable while the old `OverviewView` still renders. Verified by build only.

- [ ] **Step 1: Add `SetRegion` to `MapWidgetViewModel`**

In `dotnet/src/TelemetryPoc.App/ViewModels/MapWidgetViewModel.cs`, add a method after `EnsureRegion`:

```csharp
    /// <summary>Replace the viewport (interactive pan/zoom owns it after the first fit).</summary>
    public void SetRegion(Region r) => Region = r;
```

- [ ] **Step 2: Create `WidgetViewModel`**

Create `dotnet/src/TelemetryPoc.App/ViewModels/WidgetViewModel.cs`:

```csharp
using System.ComponentModel;
using TelemetryPoc.App.Viz;

namespace TelemetryPoc.App.ViewModels;

/// <summary>One cell in the dashboard grid. Holds the inner content VM
/// (Gauge/Line/Map) plus its grid placement; exposes pixel geometry for the Canvas.</summary>
public sealed class WidgetViewModel : INotifyPropertyChanged
{
    public string Id { get; }

    public WidgetViewModel(string id, WidgetKind kind, object content, int col, int row, int cols, int rows)
    {
        Id = id; _kind = kind; _content = content;
        _col = col; _row = row; _cols = cols; _rows = rows;
    }

    private WidgetKind _kind;
    public WidgetKind Kind
    {
        get => _kind;
        set { _kind = value; Raise(nameof(Kind)); Raise(nameof(IsToggleable)); Raise(nameof(IsRemovable)); }
    }

    private object _content;
    public object Content { get => _content; set { _content = value; Raise(nameof(Content)); } }

    private int _col, _row, _cols, _rows;
    public int Col { get => _col; set { _col = value; Raise(nameof(Col)); Raise(nameof(Left)); } }
    public int Row { get => _row; set { _row = value; Raise(nameof(Row)); Raise(nameof(Top)); } }
    public int Cols { get => _cols; set { _cols = value; Raise(nameof(Cols)); Raise(nameof(Width)); } }
    public int Rows { get => _rows; set { _rows = value; Raise(nameof(Rows)); Raise(nameof(Height)); } }

    // Canvas geometry in DIPs: Left/Top on pitch, size = cells*158 + gaps*10 = 168*n - 10.
    public double Left => (_col - 1) * WidgetLayout.Pitch;
    public double Top => (_row - 1) * WidgetLayout.Pitch;
    public double Width => _cols * WidgetLayout.Pitch - WidgetLayout.Gap;
    public double Height => _rows * WidgetLayout.Pitch - WidgetLayout.Gap;

    public bool IsToggleable => _kind != WidgetKind.Map;
    public bool IsRemovable => _kind != WidgetKind.Map;
    public string ToggleLabel => _kind == WidgetKind.Gauge ? "LINE" : "GAUGE";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        if (n == nameof(Kind)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToggleLabel)));
    }
}
```

- [ ] **Step 3: Create `DashboardViewModel`**

Create `dotnet/src/TelemetryPoc.App/ViewModels/DashboardViewModel.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TelemetryPoc.App.Viz;
using TelemetryPoc.Core;

namespace TelemetryPoc.App.ViewModels;

/// <summary>Owns the dashboard widget collection. All geometry decisions delegate to
/// WidgetLayout (pure, tested); this class only instantiates content VMs and mutates
/// placement. In-memory only — rebuilt from channels on each meta load.</summary>
public sealed class DashboardViewModel
{
    private readonly RideSession _session;
    private readonly MapWidgetViewModel _map;
    private readonly Dictionary<int, ChannelMeta> _byId = new();
    private int _nextId = 1;

    public ObservableCollection<WidgetViewModel> Widgets { get; } = new();

    public DashboardViewModel(RideSession session)
    {
        _session = session;
        _map = new MapWidgetViewModel(session);
        _session.MetaLoaded += Build;
        _session.Ticked += Refresh;
        _session.Ticked += _map.Tick;
        _session.Reset += OnReset;
    }

    private void Build()
    {
        Widgets.Clear();
        _byId.Clear();
        var store = _session.Store;
        foreach (var ch in store.Channels) _byId[ch.Id] = ch;

        foreach (var w in WidgetSeed.SeedLayout(store.Channels))
            Widgets.Add(new WidgetViewModel(w.Id, w.Kind, ContentFor(w.Kind, w.ChannelId), w.Col, w.Row, w.Cols, w.Rows));
        Refresh();
    }

    private object ContentFor(WidgetKind kind, int? channelId) => kind switch
    {
        WidgetKind.Map => _map,
        WidgetKind.Line => new LineChartViewModel(_byId[channelId!.Value]),
        _ => new GaugeViewModel(_byId[channelId!.Value]),
    };

    private void Refresh()
    {
        var store = _session.Store;
        foreach (var w in Widgets)
            switch (w.Content)
            {
                case GaugeViewModel g: g.Refresh(store); break;
                case LineChartViewModel l: l.Refresh(store); break;
            }
    }

    private void OnReset()
    {
        foreach (var w in Widgets)
        {
            if (w.Content is LineChartViewModel l) l.RaiseReset();
            if (w.Content is MapWidgetViewModel m) m.RaiseReset();
        }
    }

    public void AddGauge(int channelId, int col, int row)
    {
        if (!_byId.TryGetValue(channelId, out var ch)) return;
        var id = $"w-{_nextId++}";
        var w = new WidgetViewModel(id, WidgetKind.Gauge, new GaugeViewModel(ch), col, row, 1, 1);
        Widgets.Add(w);
        Refresh();
    }

    public void Move(string id, int col, int row)
    {
        var w = Find(id); if (w is null) return;
        w.Col = col; w.Row = row;
    }

    public void Resize(string id, int cols, int rows)
    {
        var w = Find(id); if (w is null) return;
        var (c, r) = WidgetLayout.ClampSize(w.Kind, cols, rows);
        w.Cols = c; w.Rows = r;
    }

    public void Toggle(string id)
    {
        var w = Find(id); if (w is null || !w.IsToggleable) return;
        var content = w.Content;
        int channelId = content switch
        {
            GaugeViewModel => ChannelOf(content),
            LineChartViewModel => ChannelOf(content),
            _ => -1,
        };
        if (channelId < 0 || !_byId.TryGetValue(channelId, out var ch)) return;
        var (kind, cols, rows) = WidgetLayout.Toggle(w.Kind, w.Cols, w.Rows);
        w.Content = kind == WidgetKind.Line ? new LineChartViewModel(ch) : (object)new GaugeViewModel(ch);
        w.Kind = kind; w.Cols = cols; w.Rows = rows;
        Refresh();
    }

    public void Remove(string id)
    {
        var w = Find(id); if (w is null || !w.IsRemovable) return;
        Widgets.Remove(w);
    }

    public void ZoomBy(string id, double factor)
    {
        var w = Find(id); if (w is null) return;
        if (w.Content is LineChartViewModel l) l.ZoomBy(factor);
    }

    private WidgetViewModel? Find(string id) => Widgets.FirstOrDefault(x => x.Id == id);

    // Inner content VMs expose Name/Unit but not channel id; resolve via the metadata map.
    private int ChannelOf(object content)
    {
        var name = content switch
        {
            GaugeViewModel g => g.Name,
            LineChartViewModel l => l.Name,
            _ => null,
        };
        if (name is null) return -1;
        foreach (var ch in _byId.Values) if (ch.Name == name) return ch.Id;
        return -1;
    }
}
```

> The `ChannelOf` name lookup is a deliberate small indirection so `WidgetViewModel` stays free of channel ids. Channel names are unique in the ride schema; if a future schema allows duplicates, store the channel id on `WidgetViewModel` instead.

- [ ] **Step 4: Add `ZoomBy`/`ResetZoom` stubs to `LineChartViewModel`**

> Task 8 fully implements line zoom; `DashboardViewModel.ZoomBy` references `LineChartViewModel.ZoomBy` now, so add the method here to keep the build green. In `dotnet/src/TelemetryPoc.App/ViewModels/LineChartViewModel.cs`, change the window constant to a field and add the methods:

Replace `private const long WindowMs = 60_000;` with:

```csharp
    private int _zoom = 1;
    public int Zoom => _zoom;
    private long WindowMs => 60_000 / _zoom;

    public void ZoomBy(double factor) => _zoom = WidgetLayout.ZoomBy(_zoom, factor);
    public void ResetZoom() => _zoom = 1;
```

Add `using TelemetryPoc.App.Viz;` if not already present (the file already imports it).

- [ ] **Step 5: Build to verify it compiles**

Run: `cd dotnet && dotnet build TelemetryPoc.slnx`
Expected: Build succeeded, 0 errors. (The new VMs are unreferenced by any view yet — that is fine.)

- [ ] **Step 6: Run the full test suite (no regressions)**

Run: `cd dotnet && dotnet test`
Expected: PASS (previous count + the new pure tests; no VM tests added).

- [ ] **Step 7: Commit**

```bash
git add dotnet/src/TelemetryPoc.App/ViewModels/WidgetViewModel.cs dotnet/src/TelemetryPoc.App/ViewModels/DashboardViewModel.cs dotnet/src/TelemetryPoc.App/ViewModels/MapWidgetViewModel.cs dotnet/src/TelemetryPoc.App/ViewModels/LineChartViewModel.cs
git commit -m "feat(dotnet): dashboard + widget view-models (grid state)"
```

---

### Task 5: Widget grid view + swap into OverviewView

**Files:**
- Create: `dotnet/src/TelemetryPoc.App/Views/WidgetGridView.xaml` + `.xaml.cs`
- Modify: `dotnet/src/TelemetryPoc.App/ViewModels/OverviewViewModel.cs`
- Modify: `dotnet/src/TelemetryPoc.App/Views/OverviewView.xaml`
- Modify: `dotnet/src/TelemetryPoc.App/Views/GaugeView.xaml`, `LineChartView.xaml`, `MapWidgetView.xaml` (remove fixed size + internal title)

**Interfaces:**
- Consumes: `DashboardViewModel.Widgets`, `WidgetViewModel` (Task 4); existing `GaugeView`/`LineChartView`/`MapWidgetView`.
- Produces: `OverviewViewModel.Dashboard` (a `DashboardViewModel`); a `WidgetGridView` that renders `Widgets` on a Canvas with per-cell header controls. No interaction handlers yet (Tasks 6–7); this task delivers static rendering of the seeded layout.

- [ ] **Step 1: Wire `Dashboard` into `OverviewViewModel`, drop old region collections**

Edit `dotnet/src/TelemetryPoc.App/ViewModels/OverviewViewModel.cs`. Remove `Gauges`, `LineCharts`, `MapWidget` and their wiring; add `Dashboard`. New file content:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using TelemetryPoc.App.Viz;
using TelemetryPoc.Core;

namespace TelemetryPoc.App.ViewModels;

public sealed class OverviewViewModel : INotifyPropertyChanged
{
    private readonly RideSession _session;
    public ObservableCollection<ParamGroupViewModel> Groups { get; } = new();
    public DashboardViewModel Dashboard { get; }

    private string _channelCountText = "ALL · 0 CH";
    public string ChannelCountText
    {
        get => _channelCountText;
        private set { _channelCountText = value; Raise(nameof(ChannelCountText)); }
    }

    public OverviewViewModel(RideSession session)
    {
        _session = session;
        Dashboard = new DashboardViewModel(session);
        _session.MetaLoaded += BuildGroups;
        _session.Ticked += RefreshRows;
    }

    private void BuildGroups()
    {
        Groups.Clear();
        var store = _session.Store;
        foreach (var g in ParamGrouping.Group(store.Channels))
            Groups.Add(new ParamGroupViewModel(g.Name, g.Channels));
        ChannelCountText = string.Format(CultureInfo.InvariantCulture, "ALL · {0} CH", store.Channels.Count);
        RefreshRows();
    }

    private void RefreshRows()
    {
        foreach (var g in Groups) g.Refresh(_session.Store);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
```

- [ ] **Step 2: Strip fixed size + internal name title from the content views**

`dotnet/src/TelemetryPoc.App/Views/GaugeView.xaml` — remove `Width="160" Height="170"` from the `UserControl` line, and delete the top name `TextBlock` (line 6–7, the `DockPanel.Dock="Top"` Name block). Result header line:

```xml
<UserControl x:Class="TelemetryPoc.App.Views.GaugeView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="{StaticResource Panel}">
    <DockPanel Margin="6">
        <Grid DockPanel.Dock="Bottom" HorizontalAlignment="Center" Margin="0,2,0,0">
```

(everything from the `<Grid DockPanel.Dock="Bottom"` down is unchanged.)

`dotnet/src/TelemetryPoc.App/Views/LineChartView.xaml` — remove `Width="380" Height="190"` and delete the top title `Grid` (the `DockPanel.Dock="Top"` block, lines 7–12). Result:

```xml
<UserControl x:Class="TelemetryPoc.App.Views.LineChartView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:sp="clr-namespace:ScottPlot.WPF;assembly=ScottPlot.WPF"
             Background="{StaticResource Panel}">
    <DockPanel Margin="4">
        <sp:WpfPlot x:Name="Plot" />
    </DockPanel>
</UserControl>
```

`dotnet/src/TelemetryPoc.App/Views/MapWidgetView.xaml` — delete the `FLIGHT TRACK` `TextBlock` (line 7–8) so only the `SKElement` fills the cell:

```xml
<UserControl x:Class="TelemetryPoc.App.Views.MapWidgetView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:skia="clr-namespace:SkiaSharp.Views.WPF;assembly=SkiaSharp.Views.WPF"
             Background="{StaticResource Panel}">
    <DockPanel>
        <skia:SKElement x:Name="Skia" PaintSurface="OnPaintSurface" />
    </DockPanel>
</UserControl>
```

- [ ] **Step 3: Create `WidgetGridView`**

Create `dotnet/src/TelemetryPoc.App/Views/WidgetGridView.xaml`. Implicit `DataTemplate`s map each content VM to its view; the cell chrome (header grip + toggle + ×, and a resize grip) wraps the content. Handlers are named now but implemented in Tasks 6–7; this task wires only what is needed to render.

```xml
<UserControl x:Class="TelemetryPoc.App.Views.WidgetGridView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:TelemetryPoc.App.ViewModels"
             xmlns:views="clr-namespace:TelemetryPoc.App.Views"
             Background="{StaticResource Bg}">
    <UserControl.Resources>
        <DataTemplate DataType="{x:Type vm:GaugeViewModel}"><views:GaugeView /></DataTemplate>
        <DataTemplate DataType="{x:Type vm:LineChartViewModel}"><views:LineChartView /></DataTemplate>
        <DataTemplate DataType="{x:Type vm:MapWidgetViewModel}"><views:MapWidgetView /></DataTemplate>
    </UserControl.Resources>
    <ScrollViewer x:Name="Scroll" HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Auto"
                  AllowDrop="True" Drop="OnCanvasDrop" DragOver="OnCanvasDragOver">
        <ItemsControl ItemsSource="{Binding Widgets}">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <Canvas x:Name="GridCanvas" Width="1600" Height="1600" Background="Transparent" />
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemContainerStyle>
                <Style TargetType="ContentPresenter">
                    <Setter Property="Canvas.Left" Value="{Binding Left}" />
                    <Setter Property="Canvas.Top" Value="{Binding Top}" />
                </Style>
            </ItemsControl.ItemContainerStyle>
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border Width="{Binding Width}" Height="{Binding Height}" Margin="0"
                            Background="{StaticResource Panel}" BorderBrush="{StaticResource Border1}" BorderThickness="1">
                        <Grid>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto" />
                                <RowDefinition Height="*" />
                            </Grid.RowDefinitions>
                            <!-- header: grip (drag-move) + controls -->
                            <Grid Grid.Row="0" Background="{StaticResource PanelHeader}">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <TextBlock x:Name="Grip" Grid.Column="0" Text="☰" Cursor="SizeAll"
                                           Foreground="{StaticResource PanelTitle}" FontSize="11" Padding="6,2"
                                           MouseLeftButtonDown="OnHeaderDown" />
                                <Button Grid.Column="1" Content="{Binding ToggleLabel}" Click="OnToggle"
                                        Visibility="{Binding IsToggleable, Converter={StaticResource BoolToVis}}"
                                        Foreground="{StaticResource TextDim}" Background="Transparent" BorderThickness="0"
                                        FontSize="9" Padding="6,2" Cursor="Hand" />
                                <Button Grid.Column="2" Content="✕" Click="OnRemove"
                                        Visibility="{Binding IsRemovable, Converter={StaticResource BoolToVis}}"
                                        Foreground="{StaticResource Red}" Background="Transparent" BorderThickness="0"
                                        FontSize="9" Padding="6,2" Cursor="Hand" />
                            </Grid>
                            <!-- body: content VM → implicit template -->
                            <ContentControl Grid.Row="1" Content="{Binding Content}" />
                            <!-- resize grip, bottom-right -->
                            <Thumb x:Name="ResizeThumb" Grid.Row="1" Width="14" Height="14"
                                   HorizontalAlignment="Right" VerticalAlignment="Bottom" Cursor="SizeNWSE"
                                   DragStarted="OnResizeStart" DragDelta="OnResizeDelta" Opacity="0.6" />
                        </Grid>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </ScrollViewer>
</UserControl>
```

> `BoolToVis` is WPF's standard `BooleanToVisibilityConverter`. Add it once to `App.xaml` resources (Step 4).

- [ ] **Step 4: Add the converter to `App.xaml`**

In `dotnet/src/TelemetryPoc.App/App.xaml`, inside `<Application.Resources>` (merge alongside the theme), add:

```xml
<BooleanToVisibilityConverter x:Key="BoolToVis" />
```

> If `App.xaml` uses a `ResourceDictionary` with `MergedDictionaries`, place the converter key as a sibling of `MergedDictionaries` inside the same dictionary. Verify it resolves at build.

- [ ] **Step 5: Create the code-behind with stub handlers**

Create `dotnet/src/TelemetryPoc.App/Views/WidgetGridView.xaml.cs`. Stubs compile now; Tasks 6–7 fill them in.

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace TelemetryPoc.App.Views;

public partial class WidgetGridView : UserControl
{
    public WidgetGridView() => InitializeComponent();

    private void OnCanvasDragOver(object sender, DragEventArgs e) { }
    private void OnCanvasDrop(object sender, DragEventArgs e) { }
    private void OnHeaderDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { }
    private void OnResizeStart(object sender, DragStartedEventArgs e) { }
    private void OnResizeDelta(object sender, DragDeltaEventArgs e) { }
    private void OnToggle(object sender, RoutedEventArgs e) { }
    private void OnRemove(object sender, RoutedEventArgs e) { }
}
```

- [ ] **Step 6: Swap `WidgetGridView` into `OverviewView`**

Replace the `<Grid Grid.Column="1">…</Grid>` block (lines 12–48) in `dotnet/src/TelemetryPoc.App/Views/OverviewView.xaml` with a single grid view bound to `Dashboard`:

```xml
        <views:WidgetGridView Grid.Column="1" DataContext="{Binding Dashboard}" Margin="6" />
```

(Keep the outer `<Grid>` with the two `ColumnDefinition`s and `<views:ParamPanel Grid.Column="0" Margin="6" />`.)

- [ ] **Step 7: Build**

Run: `cd dotnet && dotnet build TelemetryPoc.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Launch and visually verify**

Run: `cd dotnet && dotnet run --project src/TelemetryPoc.App`
Expected: the dashboard shows the seeded widgets (map 4×4, gauges 1×1, lines 2×1) laid out on the grid, each with a header strip (`☰`, toggle label, `✕`) and live data. The PARAMETERS sidebar is unchanged. Compare against the Rust app + `docs/reference/dashboard-target.md`. Buttons/grips do nothing yet (expected).

- [ ] **Step 9: Run the full suite**

Run: `cd dotnet && dotnet test`
Expected: PASS (unchanged count — this task adds no tests).

- [ ] **Step 10: Commit**

```bash
git add dotnet/src/TelemetryPoc.App/Views/WidgetGridView.xaml dotnet/src/TelemetryPoc.App/Views/WidgetGridView.xaml.cs dotnet/src/TelemetryPoc.App/Views/OverviewView.xaml dotnet/src/TelemetryPoc.App/Views/GaugeView.xaml dotnet/src/TelemetryPoc.App/Views/LineChartView.xaml dotnet/src/TelemetryPoc.App/Views/MapWidgetView.xaml dotnet/src/TelemetryPoc.App/ViewModels/OverviewViewModel.cs dotnet/src/TelemetryPoc.App/App.xaml
git commit -m "feat(dotnet): unified widget grid view replaces fixed regions"
```

---

### Task 6: Drag-to-create + toggle + remove

**Files:**
- Modify: `dotnet/src/TelemetryPoc.App/Views/ParamPanel.xaml` + create `ParamPanel.xaml.cs` (drag source)
- Modify: `dotnet/src/TelemetryPoc.App/ViewModels/ParamRowViewModel.cs` (expose channel id)
- Modify: `dotnet/src/TelemetryPoc.App/Views/WidgetGridView.xaml.cs` (drop + toggle + remove)

**Interfaces:**
- Consumes: `DashboardViewModel.AddGauge/Toggle/Remove`; `WidgetLayout.CellFromPoint`; `ParamRowViewModel`.
- Produces: a DataObject of format `"inu-channel"` carrying an `int` channel id, dragged from a param row; `WidgetGridView` drop creates a gauge at the snapped cell.

- [ ] **Step 1: Expose channel id on `ParamRowViewModel`**

In `dotnet/src/TelemetryPoc.App/ViewModels/ParamRowViewModel.cs`, add a public `ChannelId` (the row already wraps a `ChannelMeta`; surface its id). If the field is `_ch`, add:

```csharp
    public int ChannelId => _ch.Id;
```

> Open the file to confirm the backing field name; if it stores the id differently, expose that. The property must return the channel's integer id.

- [ ] **Step 2: Make param rows drag sources**

Add `MouseMove="OnRowDrag"` to the row `Border` in `dotnet/src/TelemetryPoc.App/Views/ParamPanel.xaml` (the `<Border Background="{Binding RowBackground}" Padding="10,2">` at line 27):

```xml
<Border Background="{Binding RowBackground}" Padding="10,2" MouseMove="OnRowDrag">
```

Add `x:Class` already present; create `dotnet/src/TelemetryPoc.App/Views/ParamPanel.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TelemetryPoc.App.ViewModels;

namespace TelemetryPoc.App.Views;

public partial class ParamPanel : UserControl
{
    public ParamPanel() => InitializeComponent();

    private void OnRowDrag(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is FrameworkElement fe && fe.DataContext is ParamRowViewModel row)
        {
            var data = new DataObject("inu-channel", row.ChannelId);
            DragDrop.DoDragDrop(fe, data, DragDropEffects.Copy);
        }
    }
}
```

> If `ParamPanel.xaml` has no `x:Class` partial backing yet, it does (`x:Class="TelemetryPoc.App.Views.ParamPanel"` at line 1), so this partial binds correctly.

- [ ] **Step 3: Implement drop + toggle + remove in the grid**

Replace the stub bodies in `dotnet/src/TelemetryPoc.App/Views/WidgetGridView.xaml.cs` for `OnCanvasDragOver`, `OnCanvasDrop`, `OnToggle`, `OnRemove` (leave move/resize stubs for Task 7):

```csharp
    private void OnCanvasDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("inu-channel") ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnCanvasDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not ViewModels.DashboardViewModel dvm) return;
        if (!e.Data.GetDataPresent("inu-channel")) return;
        var channelId = (int)e.Data.GetData("inu-channel");
        var canvas = FindCanvas();
        if (canvas is null) return;
        var p = e.GetPosition(canvas);
        var (col, row) = TelemetryPoc.App.Viz.WidgetLayout.CellFromPoint(p.X, p.Y);
        dvm.AddGauge(channelId, col, row);
    }

    private void OnToggle(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.DashboardViewModel dvm && Widget(sender) is { } w) dvm.Toggle(w.Id);
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.DashboardViewModel dvm && Widget(sender) is { } w) dvm.Remove(w.Id);
    }

    private static ViewModels.WidgetViewModel? Widget(object sender)
        => (sender as FrameworkElement)?.DataContext as ViewModels.WidgetViewModel;

    private Canvas? FindCanvas()
    {
        // The ItemsControl's ItemsPanel Canvas; walk the visual tree from the ScrollViewer content.
        return FindVisualChild<Canvas>(this);
    }

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (c is T t) return t;
            var r = FindVisualChild<T>(c);
            if (r is not null) return r;
        }
        return null;
    }
```

Add `using System.Windows.Controls;` (already present) — ensure `Canvas`, `FrameworkElement` resolve.

- [ ] **Step 4: Build**

Run: `cd dotnet && dotnet build TelemetryPoc.slnx`
Expected: Build succeeded.

- [ ] **Step 5: Launch and verify**

Run: `cd dotnet && dotnet run --project src/TelemetryPoc.App`
Expected: drag a row from PARAMETERS onto the grid → a new 1×1 gauge appears at the drop cell, live. Click a gauge's `LINE` button → it becomes a line chart (≥2×1) and back with `GAUGE`. Click `✕` → the widget disappears. Map has no toggle/remove buttons.

- [ ] **Step 6: Commit**

```bash
git add dotnet/src/TelemetryPoc.App/Views/ParamPanel.xaml dotnet/src/TelemetryPoc.App/Views/ParamPanel.xaml.cs dotnet/src/TelemetryPoc.App/ViewModels/ParamRowViewModel.cs dotnet/src/TelemetryPoc.App/Views/WidgetGridView.xaml.cs
git commit -m "feat(dotnet): drag param to create widget + gauge/chart toggle + remove"
```

---

### Task 7: Move + resize widgets

**Files:**
- Modify: `dotnet/src/TelemetryPoc.App/Views/WidgetGridView.xaml.cs`

**Interfaces:**
- Consumes: `WidgetLayout.CellFromPoint`, `WidgetLayout.ResizeStep`, `DashboardViewModel.Move/Resize`, `WidgetViewModel.Col/Row/Cols/Rows`.

- [ ] **Step 1: Implement header-drag move**

Replace the `OnHeaderDown` stub in `dotnet/src/TelemetryPoc.App/Views/WidgetGridView.xaml.cs`. Capture the mouse on the grip, track movement, drop into the snapped cell on release:

```csharp
    private ViewModels.WidgetViewModel? _moving;

    private void OnHeaderDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ViewModels.WidgetViewModel w) return;
        _moving = w;
        fe.CaptureMouse();
        fe.MouseMove += OnHeaderMove;
        fe.MouseLeftButtonUp += OnHeaderUp;
        e.Handled = true;
    }

    private void OnHeaderMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_moving is null) return;
        var canvas = FindCanvas();
        if (canvas is null) return;
        var p = e.GetPosition(canvas);
        var (col, row) = TelemetryPoc.App.Viz.WidgetLayout.CellFromPoint(p.X, p.Y);
        if (DataContext is ViewModels.DashboardViewModel dvm) dvm.Move(_moving.Id, col, row);
    }

    private void OnHeaderUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            fe.ReleaseMouseCapture();
            fe.MouseMove -= OnHeaderMove;
            fe.MouseLeftButtonUp -= OnHeaderUp;
        }
        _moving = null;
    }
```

> Live-follow move (updating the cell each `MouseMove`) mirrors the Rust feel where the widget tracks the pointer. No collision check — overlaps are allowed by design.

- [ ] **Step 2: Implement corner resize**

Replace `OnResizeStart` / `OnResizeDelta`. The `Thumb` reports cumulative delta via `DragDeltaEventArgs`; accumulate horizontal/vertical pixels and convert to cell steps from the size at drag start:

```csharp
    private int _resizeStartCols, _resizeStartRows;
    private double _accumX, _accumY;

    private void OnResizeStart(object sender, DragStartedEventArgs e)
    {
        if (Widget(sender) is not { } w) return;
        _resizeStartCols = w.Cols; _resizeStartRows = w.Rows;
        _accumX = 0; _accumY = 0;
    }

    private void OnResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (Widget(sender) is not { } w || DataContext is not ViewModels.DashboardViewModel dvm) return;
        _accumX += e.HorizontalChange;
        _accumY += e.VerticalChange;
        var cols = _resizeStartCols + TelemetryPoc.App.Viz.WidgetLayout.ResizeStep(_accumX);
        var rows = _resizeStartRows + TelemetryPoc.App.Viz.WidgetLayout.ResizeStep(_accumY);
        dvm.Resize(w.Id, cols, rows);
    }
```

- [ ] **Step 3: Build**

Run: `cd dotnet && dotnet build TelemetryPoc.slnx`
Expected: Build succeeded.

- [ ] **Step 4: Launch and verify**

Run: `cd dotnet && dotnet run --project src/TelemetryPoc.App`
Expected: dragging a widget's `☰` header moves it cell-by-cell following the pointer; dragging the bottom-right grip resizes in grid steps (small jitters under ~24px do nothing — the deadzone), clamped per kind (gauge stays square, line ≤6×4, map 2–8×2–6).

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/TelemetryPoc.App/Views/WidgetGridView.xaml.cs
git commit -m "feat(dotnet): drag-move + corner-resize widgets"
```

---

### Task 8: Line chart zoom (context menu)

**Files:**
- Modify: `dotnet/src/TelemetryPoc.App/Views/WidgetGridView.xaml` (context menu on line cells)
- Modify: `dotnet/src/TelemetryPoc.App/Views/WidgetGridView.xaml.cs` (menu handlers)
- Modify: `dotnet/src/TelemetryPoc.App/Views/LineChartView.xaml.cs` (disable ScottPlot default mouse zoom/pan)

**Interfaces:**
- Consumes: `DashboardViewModel.ZoomBy`; `LineChartViewModel.ZoomBy/ResetZoom/Zoom` (Task 4); `WidgetKind`.

> `LineChartViewModel.ZoomBy/ResetZoom` and `WindowMs = 60000/zoom` already exist from Task 4. The visible window updates automatically: `Refresh` recomputes `WindowMin/Max` from the new `WindowMs`, and `LineChartView.Redraw` applies `SetLimitsX`.

- [ ] **Step 1: Disable ScottPlot built-in interaction**

In `dotnet/src/TelemetryPoc.App/Views/LineChartView.xaml.cs`, at the end of `StylePlot()` (after `Plot.Refresh();`), disable the default mouse so only our window model controls the X range:

```csharp
        Plot.UserInputProcessor.IsEnabled = false; // window-based zoom only (Rust parity)
```

> ScottPlot.WPF 5.0.55 exposes `WpfPlot.UserInputProcessor.IsEnabled`. If that member differs in the pinned version, use the equivalent disable call (e.g. `Plot.Interaction.Disable()`); verify against the referenced ScottPlot version at build.

- [ ] **Step 2: Add a context menu to the line cell body**

In `dotnet/src/TelemetryPoc.App/Views/WidgetGridView.xaml`, give the body `ContentControl` a context menu that shows only for line widgets. Replace the `<ContentControl Grid.Row="1" Content="{Binding Content}" />` line with:

```xml
                            <ContentControl Grid.Row="1" Content="{Binding Content}">
                                <ContentControl.ContextMenu>
                                    <ContextMenu Visibility="{Binding PlacementTarget.DataContext.IsLine,
                                                 RelativeSource={RelativeSource Self}, Converter={StaticResource BoolToVis}}">
                                        <MenuItem Header="Zoom in"  Click="OnZoomIn" />
                                        <MenuItem Header="Zoom out" Click="OnZoomOut" />
                                        <MenuItem Header="Reset"    Click="OnZoomReset" />
                                    </ContextMenu>
                                </ContentControl.ContextMenu>
                            </ContentControl>
```

Add an `IsLine` helper to `WidgetViewModel` (in `dotnet/src/TelemetryPoc.App/ViewModels/WidgetViewModel.cs`), raised alongside `Kind`:

```csharp
    public bool IsLine => _kind == WidgetKind.Line;
```

And in `WidgetViewModel.Kind`'s setter, add `Raise(nameof(IsLine));`.

- [ ] **Step 3: Implement the menu handlers**

Add to `dotnet/src/TelemetryPoc.App/Views/WidgetGridView.xaml.cs`:

```csharp
    private void OnZoomIn(object sender, RoutedEventArgs e) => Zoom(sender, 2.0);
    private void OnZoomOut(object sender, RoutedEventArgs e) => Zoom(sender, 0.5);
    private void OnZoomReset(object sender, RoutedEventArgs e)
    {
        if (MenuWidget(sender) is { Content: ViewModels.LineChartViewModel l }) { l.ResetZoom(); }
    }

    private void Zoom(object sender, double factor)
    {
        if (DataContext is ViewModels.DashboardViewModel dvm && MenuWidget(sender) is { } w) dvm.ZoomBy(w.Id, factor);
    }

    private static ViewModels.WidgetViewModel? MenuWidget(object sender)
    {
        // MenuItem.DataContext is the WidgetViewModel (inherited through the ContextMenu's PlacementTarget).
        var mi = sender as FrameworkElement;
        if (mi?.DataContext is ViewModels.WidgetViewModel w) return w;
        return null;
    }
```

> A zoom change takes visible effect on the next frame `Refresh` (the window recomputes from the new `Zoom`). `ResetZoom` likewise. No redraw call is needed here — the per-tick pipeline applies it.

- [ ] **Step 4: Build**

Run: `cd dotnet && dotnet build TelemetryPoc.slnx`
Expected: Build succeeded.

- [ ] **Step 5: Launch and verify**

Run: `cd dotnet && dotnet run --project src/TelemetryPoc.App`
Expected: right-click a line chart → menu with Zoom in / Zoom out / Reset. Zoom in halves the visible time window (faster horizontal scroll), Reset returns to 60s. The mouse no longer pans/zooms the plot directly. Gauges/map show no such menu.

- [ ] **Step 6: Commit**

```bash
git add dotnet/src/TelemetryPoc.App/Views/WidgetGridView.xaml dotnet/src/TelemetryPoc.App/Views/WidgetGridView.xaml.cs dotnet/src/TelemetryPoc.App/Views/LineChartView.xaml.cs dotnet/src/TelemetryPoc.App/ViewModels/WidgetViewModel.cs
git commit -m "feat(dotnet): line chart zoom via context menu"
```

---

### Task 9: Line chart hover tooltip

**Files:**
- Create: `dotnet/src/TelemetryPoc.App.Viz/NearestSample.cs`
- Test: `dotnet/tests/TelemetryPoc.Core.Tests/NearestSampleTests.cs`
- Modify: `dotnet/src/TelemetryPoc.App/Views/LineChartView.xaml` (overlay tooltip) + `.xaml.cs` (hover)

**Interfaces:**
- Consumes: `LineChartViewModel.XsSeconds/Ys/Unit/Name`; ScottPlot pixel↔data mapping.
- Produces: `NearestSample.IndexOf(double[] xs, double xTarget) → int` (closest index, -1 if empty).

- [ ] **Step 1: Write the failing test**

Create `dotnet/tests/TelemetryPoc.Core.Tests/NearestSampleTests.cs`:

```csharp
using TelemetryPoc.App.Viz;
using Xunit;

public class NearestSampleTests
{
    [Fact]
    public void Empty_returns_minus_one()
        => Assert.Equal(-1, NearestSample.IndexOf(System.Array.Empty<double>(), 5));

    [Theory]
    [InlineData(2.4, 2)]   // closest to xs[2]=2
    [InlineData(2.6, 3)]   // closest to xs[3]=3
    [InlineData(-5, 0)]    // below range → first
    [InlineData(99, 5)]    // above range → last
    public void Finds_closest_index(double target, int expected)
    {
        var xs = new double[] { 0, 1, 2, 3, 4, 5 };
        Assert.Equal(expected, NearestSample.IndexOf(xs, target));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd dotnet && dotnet test --filter "FullyQualifiedName~NearestSampleTests"`
Expected: FAIL — `NearestSample` missing.

- [ ] **Step 3: Implement**

Create `dotnet/src/TelemetryPoc.App.Viz/NearestSample.cs`:

```csharp
namespace TelemetryPoc.App.Viz;

/// <summary>Finds the sample index whose X is closest to a target (hover lookup).
/// Assumes xs is non-decreasing (time series).</summary>
public static class NearestSample
{
    public static int IndexOf(double[] xs, double xTarget)
    {
        if (xs.Length == 0) return -1;
        int best = 0;
        double bestDist = System.Math.Abs(xs[0] - xTarget);
        for (int i = 1; i < xs.Length; i++)
        {
            double d = System.Math.Abs(xs[i] - xTarget);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd dotnet && dotnet test --filter "FullyQualifiedName~NearestSampleTests"`
Expected: PASS.

- [ ] **Step 5: Add the tooltip overlay to `LineChartView.xaml`**

Wrap the plot in a `Grid` with a floating tooltip `Border` in `dotnet/src/TelemetryPoc.App/Views/LineChartView.xaml`:

```xml
<UserControl x:Class="TelemetryPoc.App.Views.LineChartView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:sp="clr-namespace:ScottPlot.WPF;assembly=ScottPlot.WPF"
             Background="{StaticResource Panel}">
    <Grid Margin="4">
        <sp:WpfPlot x:Name="Plot" MouseMove="OnHover" MouseLeave="OnHoverLeave" />
        <Border x:Name="Tip" Visibility="Collapsed" Background="{StaticResource Panel2}"
                BorderBrush="{StaticResource Border1}" BorderThickness="1" Padding="5,2"
                HorizontalAlignment="Left" VerticalAlignment="Top" IsHitTestVisible="False">
            <TextBlock x:Name="TipText" Foreground="{StaticResource TextData}"
                       FontFamily="{StaticResource MonoFont}" FontSize="10" />
        </Border>
    </Grid>
</UserControl>
```

- [ ] **Step 6: Implement hover in `LineChartView.xaml.cs`**

Add handlers to `dotnet/src/TelemetryPoc.App/Views/LineChartView.xaml.cs`:

```csharp
    private void OnHover(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_vm is null) { Tip.Visibility = System.Windows.Visibility.Collapsed; return; }
        var xs = _vm.XsSeconds; var ys = _vm.Ys;
        if (xs.Length == 0) { Tip.Visibility = System.Windows.Visibility.Collapsed; return; }

        var pos = e.GetPosition(Plot);
        var px = new ScottPlot.Pixel(pos.X, pos.Y);
        var coord = Plot.Plot.GetCoordinates(px);
        int i = NearestSample.IndexOf(xs, coord.X);
        if (i < 0 || i >= ys.Length) { Tip.Visibility = System.Windows.Visibility.Collapsed; return; }

        TipText.Text = $"{LineAxis.FormatElapsed(xs[i])} · {ys[i]:0.##} {_vm.Unit}";
        Tip.Margin = new System.Windows.Thickness(pos.X + 12, pos.Y + 8, 0, 0);
        Tip.Visibility = System.Windows.Visibility.Visible;
    }

    private void OnHoverLeave(object sender, System.Windows.Input.MouseEventArgs e)
        => Tip.Visibility = System.Windows.Visibility.Collapsed;
```

> `Plot.Plot.GetCoordinates(Pixel)` maps screen pixels → data coordinates in ScottPlot 5. If the pinned version names it differently, use the equivalent pixel→coordinate call on the `Plot`. The tooltip text mirrors the Rust `linechart-tip` format `"{m:ss} · {value} {unit}"`.

- [ ] **Step 7: Build**

Run: `cd dotnet && dotnet build TelemetryPoc.slnx`
Expected: Build succeeded.

- [ ] **Step 8: Launch and verify**

Run: `cd dotnet && dotnet run --project src/TelemetryPoc.App`
Expected: hovering a line chart shows a floating tooltip near the cursor reading `m:ss · value unit` for the nearest sample; leaving the plot hides it.

- [ ] **Step 9: Run the full suite**

Run: `cd dotnet && dotnet test`
Expected: PASS (with the new `NearestSampleTests`).

- [ ] **Step 10: Commit**

```bash
git add dotnet/src/TelemetryPoc.App.Viz/NearestSample.cs dotnet/tests/TelemetryPoc.Core.Tests/NearestSampleTests.cs dotnet/src/TelemetryPoc.App/Views/LineChartView.xaml dotnet/src/TelemetryPoc.App/Views/LineChartView.xaml.cs
git commit -m "feat(dotnet): line chart hover tooltip"
```

---

### Task 10: Map pan + zoom

**Files:**
- Modify: `dotnet/src/TelemetryPoc.App/Views/MapWidgetView.xaml.cs`

**Interfaces:**
- Consumes: `MapInteract.Pan/ZoomAt`, `TileMath.MinZoom/MaxZoom`, `TileMath.FitBbox` (Task 3); `MapWidgetViewModel.SetRegion`, `MapWidgetViewModel.Region` (Task 4); `RideSession.GpsBounds`.

> The map basemap currently builds once (`if (_basemap is null)`). With pan/zoom the `Region` changes, so the basemap must rebuild whenever the region differs from what was last rendered.

- [ ] **Step 1: Rebuild the basemap when the region changes; add mouse handlers**

Edit `dotnet/src/TelemetryPoc.App/Views/MapWidgetView.xaml.cs`. Track the rendered region and add wheel/drag/double-click handlers. Full new file:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using TelemetryPoc.App.ViewModels;
using TelemetryPoc.Map;

namespace TelemetryPoc.App.Views;

public partial class MapWidgetView : UserControl
{
    private MapWidgetViewModel? _vm;
    private SKPicture? _basemap;
    private Region? _renderedFor;
    private Point _lastDrag;
    private bool _dragging;

    public MapWidgetView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => { if (_vm is null) OnDataContextChanged(this, default); };
        Unloaded += (_, _) => Detach();
        Skia.MouseWheel += OnWheel;
        Skia.MouseLeftButtonDown += OnDown;
        Skia.MouseMove += OnMove;
        Skia.MouseLeftButtonUp += OnUp;
        Skia.MouseLeftButtonDown += OnMaybeDoubleClick;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();
        _vm = DataContext as MapWidgetViewModel;
        if (_vm is not null) { _vm.Updated += OnTick; _vm.Reset += OnReset; }
    }

    private void Detach()
    {
        if (_vm is not null) { _vm.Updated -= OnTick; _vm.Reset -= OnReset; }
        _vm = null;
        _basemap?.Dispose();
        _basemap = null;
        _renderedFor = null;
    }

    private void OnTick() => Skia.InvalidateVisual();
    private void OnReset() => Skia.InvalidateVisual();

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (_vm?.Region is null) return;
        var p = e.GetPosition(Skia);
        int step = e.Delta > 0 ? +1 : -1;
        _vm.SetRegion(MapInteract.ZoomAt(_vm.Region, p.X, p.Y, step, TileMath.MinZoom, TileMath.MaxZoom));
        Skia.InvalidateVisual();
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm?.Region is null) return;
        _dragging = true; _lastDrag = e.GetPosition(Skia); Skia.CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _vm?.Region is null) return;
        var p = e.GetPosition(Skia);
        var dx = p.X - _lastDrag.X; var dy = p.Y - _lastDrag.Y;
        _lastDrag = p;
        _vm.SetRegion(MapInteract.Pan(_vm.Region, dx, dy));
        Skia.InvalidateVisual();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false; Skia.ReleaseMouseCapture();
    }

    private void OnMaybeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2 || _vm is null) return;
        // re-fit to the whole-ride GPS bounds (the original FitBbox view)
        var b = _vm.GpsBoundsForRefit();
        if (b is null || _vm.Region is null) return;
        var (cLat, cLon, z) = TileMath.FitBbox(b.Value.MinLat, b.Value.MinLon, b.Value.MaxLat, b.Value.MaxLon,
                                               _vm.Region.Width, _vm.Region.Height);
        _vm.SetRegion(_vm.Region with { CenterLat = cLat, CenterLon = cLon, Zoom = z });
        Skia.InvalidateVisual();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColor.Parse(MapStyle.BackgroundHex));
        if (_vm is null) return;

        var w = e.Info.Width;
        var h = e.Info.Height;
        _vm.EnsureRegion(w, h);
        if (_vm.Region is null) return;

        if (_basemap is null || !_vm.Region.Equals(_renderedFor))
        {
            _basemap?.Dispose();
            BuildBasemap(_vm.Region, w, h);
            _renderedFor = _vm.Region;
        }
        if (_basemap is not null) canvas.DrawPicture(_basemap);

        var (lat, lon) = _vm.Track;
        TrackOverlay.Draw(canvas, _vm.Region, lat, lon);
    }

    private void BuildBasemap(Region region, int w, int h)
    {
        if (string.IsNullOrEmpty(_vm?.MbTilesPath)) { _basemap = null; return; }
        try
        {
            using var reader = new MbTilesReader(_vm.MbTilesPath);
            using var rec = new SKPictureRecorder();
            var rc = rec.BeginRecording(new SKRect(0, 0, w, h));
            BasemapRenderer.Render(rc, region, reader);
            _basemap = rec.EndRecording();
        }
        catch { _basemap = null; }
    }
}
```

- [ ] **Step 2: Expose the GPS bounds for re-fit on the map VM**

`OnMaybeDoubleClick` calls `_vm.GpsBoundsForRefit()`. Add it to `dotnet/src/TelemetryPoc.App/ViewModels/MapWidgetViewModel.cs`:

```csharp
    /// <summary>Whole-ride GPS bbox for the double-click re-fit, or null until known.</summary>
    public (double MinLat, double MinLon, double MaxLat, double MaxLon)? GpsBoundsForRefit() => _session.GpsBounds;
```

> Confirm `RideSession.GpsBounds` is a `(double MinLat,double MinLon,double MaxLat,double MaxLon)?` — `EnsureRegion` already reads `_session.GpsBounds` with `.Value.MinLat` etc., so the shape matches.

- [ ] **Step 3: Build**

Run: `cd dotnet && dotnet build TelemetryPoc.slnx`
Expected: Build succeeded.

- [ ] **Step 4: Launch and verify**

Run: `cd dotnet && dotnet run --project src/TelemetryPoc.App`
Expected: the map widget responds to the mouse wheel (zoom toward cursor, clamped to z9–14), drag-pan (the basemap + track follow the drag), and double-click (snaps back to the whole-ride fit). The track overlay stays correctly projected at every zoom/pan. Moving/resizing the map cell via its header still works (Task 7) independently of in-cell pan/zoom.

- [ ] **Step 5: Run the full suite**

Run: `cd dotnet && dotnet test`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add dotnet/src/TelemetryPoc.App/Views/MapWidgetView.xaml.cs dotnet/src/TelemetryPoc.App/ViewModels/MapWidgetViewModel.cs
git commit -m "feat(dotnet): interactive map pan + zoom"
```

---

## Self-Review (plan vs spec)

**Spec coverage:**
- Unified snap-grid + cell geometry → Tasks 1, 4, 5. ✅
- Drag-create gauge 1×1 → Task 6. ✅
- Move (header) / resize (corner, deadzone) → Task 7. ✅
- Reorder = free move, overlaps allowed → Tasks 1/7 (no collision code). ✅
- Remove + gauge↔chart toggle → Tasks 4, 6. ✅
- Seed from channels → Task 2. ✅
- Line zoom (×2/÷2, clamp 1–8, window 60000/zoom, right-click) + disable default interaction → Tasks 4, 8. ✅
- Line hover tooltip `m:ss · value unit` → Task 9. ✅
- Map pan/zoom (inverse mercator, zoom-toward-cursor, clamp 9–14, drag-pan, double-click re-fit, basemap rebuild) → Tasks 3, 10. ✅
- In-memory only, no persistence → no persistence code anywhere. ✅
- Pure-logic xUnit coverage + build-verify/launch for UI → every task split accordingly. ✅

**Type consistency:** `Widget`/`WidgetKind` defined Task 1, consumed 2/4. `WidgetLayout.{CellFromPoint,ResizeStep,ClampSize,Toggle,ZoomBy,FirstFit}` defined Tasks 1–2, used 4/6/7. `LineChartViewModel.{Zoom,ZoomBy,ResetZoom}` defined Task 4, used 8. `MapWidgetViewModel.{SetRegion,GpsBoundsForRefit}` defined Tasks 4/10. `MapInteract.{Pan,ZoomAt}` + `WebMercator.WorldToLonLat` + `TileMath.{MinZoom,MaxZoom}` defined Task 3, used 10. `NearestSample.IndexOf` defined Task 9. `WidgetViewModel.{IsLine,IsToggleable,IsRemovable,ToggleLabel}` defined Tasks 4/8. Consistent.

**Verification points to watch during execution (flagged inline):** `ChannelMeta` constructor shape (Task 2), `ParamRowViewModel` backing field (Task 6), ScottPlot 5 member names `UserInputProcessor.IsEnabled` / `GetCoordinates` (Tasks 8–9), `RideSession.GpsBounds` tuple shape (Task 10). Each task names the file to confirm against and the intended behavior, so a divergent API is adapted without changing intent.
