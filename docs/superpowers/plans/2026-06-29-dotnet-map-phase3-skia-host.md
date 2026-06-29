# .NET MVT Skia Map — Phase 3 (Skia Basemap + Track + WPF Host) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish the offline map: draw the decoded MVT tiles + labels onto a SkiaSharp canvas in the dark INU style (cached once), overlay the live GPS track, and host it in a native WPF `SKElement` cell on the OVERVIEW screen — verified against the real `israel.mbtiles`.

**Architecture:** `TelemetryPoc.Map` gains **SkiaSharp core** (no WPF): `MapProject` (gps→screen, pure), `LabelLayout` (greedy collision, pure), `BasemapRenderer` + `TrackOverlay` (draw to an `SKCanvas`). The WPF `SKElement` host lives in the App (net8.0-windows). The static-region basemap is rendered once into an `SKPicture` cache; each tick only the track overlay redraws.

**Tech Stack:** .NET 8, **SkiaSharp 2.88.9** (core in Map; `SkiaSharp.Views.WPF` in App), `Mapbox.VectorTile`, xUnit.

## Global Constraints

- `TelemetryPoc.Core` unchanged. `SkiaSharp.Views.WPF` (the `SKElement` control) goes in the **App** (net8.0-windows / WPF); `TelemetryPoc.Map` references **SkiaSharp core only** (it stays net8.0, draws to `SKCanvas`).
- Static region (Phase-design): the `Region` is computed **once** from the **whole-ride GPS bbox** (`RideSession.GpsBounds`, scanned from all loaded samples at `Start` so the region covers the entire flight — not just the points replayed so far) via `TileMath.FitBbox`; the camera never moves. No GPS bounds yet → wait (don't freeze a wrong region); genuinely no GPS channels → map stays dark (the ride always has GPS).
- Basemap rendered once → `SKPicture` cache; track overlay redraws each tick. Per-tile decode wrapped in try/catch (a bad tile is skipped, never crashes the map).
- INU style: background `#0a0e14`; fills water `#16384f` (opacity 1), landcover `#0c1118` (0.8), landuse `#111820` (0.6), building `#232d38` (0.8); lines transportation-casing `#0a0e14` w3, transportation `#5b6470` w1.5; labels place text `#aebccd`, transportation_name text `#8a99ad`, halo `#0a0e14`, `name:latin` (fallback `name`); track cyan `#38c5e0` w3, position marker green `#2fd17a`.
- mbtiles path resolves from `RIDE_MBTILES` env, else walks up from the app base dir for `tiles/israel.mbtiles`. Missing → empty basemap (dark), track still draws.

## Existing API (consumed)

- Phase 1/2 `TelemetryPoc.Map`: `Region(CenterLat,CenterLon,Zoom,Width,Height)`, `WebMercator.LonLatToWorld(lon,lat,z)`, `TileMath.VisibleTiles(region)`/`FitBbox(...)`, `TileRef(Z,X,Y,ScreenX,ScreenY)`, `MapStyle.Layers`/`BackgroundHex`, `StyleLayer(Id,SourceLayer,Kind,ColorHex,Width)`, `MbTilesReader.Read(z,x,y)→byte[]?`, `MvtBasemap.DecodeTile(bytes)→IReadOnlyList<MapFeature>`, `MapFeature(SourceLayer,Type,Rings,Props,Extent)`, `TileProject.ToScreen(tileScreenX,tileScreenY,localX,localY,extent)`.
- `RideSession` (App): `Store` (`GpsTrack()→(IReadOnlyList<double> Lat, IReadOnlyList<double> Lon)`, `Channels`), `MetaLoaded`/`Ticked`, `Start()`.
- `OverviewViewModel(RideSession)`: builds on MetaLoaded, refreshes on Ticked; exposes `Groups`/`Gauges`/`LineCharts`.

## File Structure

- `dotnet/src/TelemetryPoc.Map/TelemetryPoc.Map.csproj` (modify — add SkiaSharp).
- `dotnet/src/TelemetryPoc.Map/MapStyle.cs` (modify — add `Opacity` to `StyleLayer`).
- `dotnet/src/TelemetryPoc.Map/MapProject.cs` (new) — `GpsToScreen`, `TrackBounds`.
- `dotnet/src/TelemetryPoc.Map/LabelLayout.cs` (new) — `LabelBox`, `Place`.
- `dotnet/src/TelemetryPoc.Map/BasemapRenderer.cs` (new) — `Render(SKCanvas, Region, MbTilesReader)`.
- `dotnet/src/TelemetryPoc.Map/TrackOverlay.cs` (new) — `Draw(SKCanvas, Region, lat, lon)`.
- `dotnet/src/TelemetryPoc.App/TelemetryPoc.App.csproj` (modify — add SkiaSharp.Views.WPF).
- `dotnet/src/TelemetryPoc.App/ViewModels/MapWidgetViewModel.cs` (new).
- `dotnet/src/TelemetryPoc.App/Views/MapWidgetView.xaml` + `.xaml.cs` (new).
- `dotnet/src/TelemetryPoc.App/Views/OverviewView.xaml` (modify — add the map cell).
- `dotnet/src/TelemetryPoc.App/ViewModels/OverviewViewModel.cs` (modify — expose `MapWidget`).
- `dotnet/tests/TelemetryPoc.Core.Tests/MapProjectTests.cs`, `LabelLayoutTests.cs`, `MapStyleOpacityTests.cs` (new).

---

### Task 1: SkiaSharp dep + StyleLayer.Opacity + MapProject + LabelLayout (pure, xUnit)

**Files:**
- Modify: `TelemetryPoc.Map.csproj`, `MapStyle.cs`
- Create: `MapProject.cs`, `LabelLayout.cs`, `MapProjectTests.cs`, `LabelLayoutTests.cs`, `MapStyleOpacityTests.cs`

**Interfaces:**
- Produces:
  - `StyleLayer(..., double Opacity)` (new trailing field) + per-layer opacity in `MapStyle.Layers`.
  - `MapProject.GpsToScreen(Region r, double lat, double lon) → (double X, double Y)`
  - `MapProject.TrackBounds(IReadOnlyList<double> lat, IReadOnlyList<double> lon) → (double MinLat, double MinLon, double MaxLat, double MaxLon)`
  - `record LabelBox(string Text, double X, double Y, double W, double H)`
  - `LabelLayout.Place(IReadOnlyList<LabelBox> candidates) → IReadOnlyList<LabelBox>` (greedy: keep a candidate only if its rect doesn't overlap an already-kept one)

- [ ] **Step 1: Add SkiaSharp to Map** — `cd dotnet && dotnet add src/TelemetryPoc.Map package SkiaSharp --version 2.88.9`.

- [ ] **Step 2: Add `Opacity` to `StyleLayer`** in `MapStyle.cs` — change the record + every layer entry:

```csharp
public sealed record StyleLayer(string Id, string SourceLayer, PaintKind Kind, string ColorHex, double Width, double Opacity);

public static class MapStyle
{
    public const string BackgroundHex = "#0a0e14";

    public static IReadOnlyList<StyleLayer> Layers { get; } = new[]
    {
        new StyleLayer("water", "water", PaintKind.Fill, "#16384f", 0, 1.0),
        new StyleLayer("landcover", "landcover", PaintKind.Fill, "#0c1118", 0, 0.8),
        new StyleLayer("landuse", "landuse", PaintKind.Fill, "#111820", 0, 0.6),
        new StyleLayer("transportation-casing", "transportation", PaintKind.Line, "#0a0e14", 3.0, 1.0),
        new StyleLayer("transportation", "transportation", PaintKind.Line, "#5b6470", 1.5, 1.0),
        new StyleLayer("building", "building", PaintKind.Fill, "#232d38", 0, 0.8),
    };
}
```

- [ ] **Step 3: Write the failing tests** — `MapStyleOpacityTests.cs`:

```csharp
using System.Linq;
using TelemetryPoc.Map;
using Xunit;

public class MapStyleOpacityTests
{
    [Theory]
    [InlineData("landcover", 0.8)]
    [InlineData("landuse", 0.6)]
    [InlineData("building", 0.8)]
    [InlineData("water", 1.0)]
    public void Layers_have_fill_opacity(string id, double opacity)
        => Assert.Equal(opacity, MapStyle.Layers.First(l => l.Id == id).Opacity, 6);
}
```

`MapProjectTests.cs`:

```csharp
using TelemetryPoc.Map;
using Xunit;

public class MapProjectTests
{
    [Fact]
    public void GpsToScreen_center_is_viewport_center()
    {
        var (x, y) = MapProject.GpsToScreen(new Region(32.08, 34.78, 12, 400, 300), 32.08, 34.78);
        Assert.Equal(200.0, x, 6); // Width/2
        Assert.Equal(150.0, y, 6); // Height/2
    }

    [Fact]
    public void GpsToScreen_east_is_right_of_center()
    {
        var r = new Region(32.08, 34.78, 12, 400, 300);
        var (x, _) = MapProject.GpsToScreen(r, 32.08, 34.79); // a touch east
        Assert.True(x > 200.0);
    }

    [Fact]
    public void TrackBounds_min_max()
    {
        var lat = new[] { 32.1, 32.0, 32.2 };
        var lon = new[] { 34.8, 34.7, 34.9 };
        var (minLat, minLon, maxLat, maxLon) = MapProject.TrackBounds(lat, lon);
        Assert.Equal(32.0, minLat, 6);
        Assert.Equal(34.7, minLon, 6);
        Assert.Equal(32.2, maxLat, 6);
        Assert.Equal(34.9, maxLon, 6);
    }
}
```

`LabelLayoutTests.cs`:

```csharp
using System.Collections.Generic;
using TelemetryPoc.Map;
using Xunit;

public class LabelLayoutTests
{
    [Fact]
    public void Place_drops_overlapping_candidates()
    {
        var cands = new List<LabelBox>
        {
            new("A", 0, 0, 50, 10),
            new("B", 10, 0, 50, 10),   // overlaps A → dropped
            new("C", 100, 100, 50, 10) // clear → kept
        };
        var placed = LabelLayout.Place(cands);
        Assert.Equal(new[] { "A", "C" }, placed.Select(p => p.Text).ToArray());
    }
}
```

- [ ] **Step 4: Run → fail** — `dotnet test --filter "MapStyleOpacityTests|MapProjectTests|LabelLayoutTests"`

- [ ] **Step 5: Implement `MapProject.cs`**:

```csharp
namespace TelemetryPoc.Map;

public static class MapProject
{
    public static (double X, double Y) GpsToScreen(Region r, double lat, double lon)
    {
        var (wx, wy) = WebMercator.LonLatToWorld(lon, lat, r.Zoom);
        var (cx, cy) = WebMercator.LonLatToWorld(r.CenterLon, r.CenterLat, r.Zoom);
        return (wx - cx + r.Width / 2, wy - cy + r.Height / 2);
    }

    public static (double MinLat, double MinLon, double MaxLat, double MaxLon) TrackBounds(
        IReadOnlyList<double> lat, IReadOnlyList<double> lon)
    {
        double minLat = double.MaxValue, minLon = double.MaxValue, maxLat = double.MinValue, maxLon = double.MinValue;
        int n = Math.Min(lat.Count, lon.Count);
        for (int i = 0; i < n; i++)
        {
            if (lat[i] < minLat) minLat = lat[i];
            if (lat[i] > maxLat) maxLat = lat[i];
            if (lon[i] < minLon) minLon = lon[i];
            if (lon[i] > maxLon) maxLon = lon[i];
        }
        return (minLat, minLon, maxLat, maxLon);
    }
}
```

- [ ] **Step 6: Implement `LabelLayout.cs`**:

```csharp
namespace TelemetryPoc.Map;

public sealed record LabelBox(string Text, double X, double Y, double W, double H);

public static class LabelLayout
{
    public static IReadOnlyList<LabelBox> Place(IReadOnlyList<LabelBox> candidates)
    {
        var placed = new List<LabelBox>();
        foreach (var c in candidates)
        {
            bool overlaps = false;
            foreach (var p in placed)
            {
                if (c.X < p.X + p.W && c.X + c.W > p.X && c.Y < p.Y + p.H && c.Y + c.H > p.Y)
                { overlaps = true; break; }
            }
            if (!overlaps) placed.Add(c);
        }
        return placed;
    }
}
```

- [ ] **Step 7: Run → pass** — the filtered tests, then full `dotnet test`.

- [ ] **Step 8: Commit** — `feat(dotnet): map style opacity + GpsToScreen/TrackBounds + LabelLayout (xUnit)`

---

### Task 2: BasemapRenderer + TrackOverlay (SkiaSharp draw, build-verified)

**Files:**
- Create: `BasemapRenderer.cs`, `TrackOverlay.cs`

**Interfaces:**
- Consumes: `MapStyle`/`TileMath`/`MbTilesReader`/`MvtBasemap`/`TileProject`/`MapProject`/`LabelLayout`, SkiaSharp.
- Produces:
  - `BasemapRenderer.Render(SKCanvas canvas, Region region, MbTilesReader reader)` — draws background + geometry layers + labels.
  - `TrackOverlay.Draw(SKCanvas canvas, Region region, IReadOnlyList<double> lat, IReadOnlyList<double> lon)` — cyan polyline + position marker.

> **SkiaSharp 2.88 API** is stable; the code below compiles as-is. If a member differs, adapt until `dotnet build` is clean. No unit test here — exercised live in Task 3 against `israel.mbtiles`.

- [ ] **Step 1: Implement `BasemapRenderer.cs`**:

```csharp
using SkiaSharp;

namespace TelemetryPoc.Map;

public static class BasemapRenderer
{
    public static void Render(SKCanvas canvas, Region region, MbTilesReader reader)
    {
        canvas.Clear(SKColor.Parse(MapStyle.BackgroundHex));

        var decoded = new List<(TileRef Tile, IReadOnlyList<MapFeature> Feats)>();
        foreach (var t in TileMath.VisibleTiles(region))
        {
            try
            {
                var bytes = reader.Read(t.Z, t.X, t.Y);
                if (bytes is not null) decoded.Add((t, MvtBasemap.DecodeTile(bytes)));
            }
            catch { /* skip a corrupt tile */ }
        }

        foreach (var layer in MapStyle.Layers)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColor.Parse(layer.ColorHex).WithAlpha((byte)(layer.Opacity * 255)),
                Style = layer.Kind == PaintKind.Fill ? SKPaintStyle.Fill : SKPaintStyle.Stroke,
                StrokeWidth = (float)layer.Width,
            };
            var wantPolygon = layer.Kind == PaintKind.Fill;
            foreach (var (tile, feats) in decoded)
                foreach (var f in feats)
                {
                    if (f.SourceLayer != layer.SourceLayer) continue;
                    var isPoly = f.Type == MvtGeomType.Polygon;
                    if (wantPolygon != isPoly) continue;
                    using var path = BuildPath(tile, f);
                    canvas.DrawPath(path, paint);
                }
        }

        DrawLabels(canvas, decoded);
    }

    private static SKPath BuildPath(TileRef tile, MapFeature f)
    {
        var path = new SKPath();
        foreach (var ring in f.Rings)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                var (sx, sy) = TileProject.ToScreen(tile.ScreenX, tile.ScreenY, ring[i].X, ring[i].Y, f.Extent);
                if (i == 0) path.MoveTo((float)sx, (float)sy);
                else path.LineTo((float)sx, (float)sy);
            }
            if (f.Type == MvtGeomType.Polygon) path.Close();
        }
        return path;
    }

    private static void DrawLabels(SKCanvas canvas, List<(TileRef Tile, IReadOnlyList<MapFeature> Feats)> decoded)
    {
        using var fill = new SKPaint { IsAntialias = true, TextSize = 11, Color = SKColor.Parse("#aebccd") };
        using var roadFill = new SKPaint { IsAntialias = true, TextSize = 10, Color = SKColor.Parse("#8a99ad") };
        using var halo = new SKPaint { IsAntialias = true, TextSize = 11, Color = SKColor.Parse("#0a0e14"), Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f };

        var candidates = new List<(LabelBox Box, SKPaint Paint)>();
        foreach (var (tile, feats) in decoded)
            foreach (var f in feats)
            {
                if (f.SourceLayer != "place" && f.SourceLayer != "transportation_name") continue;
                if (!f.Props.TryGetValue("name:latin", out var name) && !f.Props.TryGetValue("name", out name)) continue;
                if (string.IsNullOrWhiteSpace(name) || f.Rings.Count == 0 || f.Rings[0].Count == 0) continue;
                var pt = f.Rings[0][f.Rings[0].Count / 2]; // a representative vertex
                var (sx, sy) = TileProject.ToScreen(tile.ScreenX, tile.ScreenY, pt.X, pt.Y, f.Extent);
                var paint = f.SourceLayer == "place" ? fill : roadFill;
                var w = paint.MeasureText(name);
                candidates.Add((new LabelBox(name, sx, sy - paint.TextSize, w, paint.TextSize), paint));
            }

        var placed = LabelLayout.Place(candidates.Select(c => c.Box).ToList());
        var placedSet = new HashSet<LabelBox>(placed);
        foreach (var (box, paint) in candidates)
        {
            if (!placedSet.Contains(box)) continue;
            canvas.DrawText(box.Text, (float)box.X, (float)(box.Y + box.H), halo);
            canvas.DrawText(box.Text, (float)box.X, (float)(box.Y + box.H), paint);
        }
    }
}
```

- [ ] **Step 2: Implement `TrackOverlay.cs`**:

```csharp
using SkiaSharp;

namespace TelemetryPoc.Map;

public static class TrackOverlay
{
    public static void Draw(SKCanvas canvas, Region region, IReadOnlyList<double> lat, IReadOnlyList<double> lon)
    {
        int n = Math.Min(lat.Count, lon.Count);
        if (n == 0) return;

        using var line = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#38c5e0"), Style = SKPaintStyle.Stroke, StrokeWidth = 3, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var path = new SKPath();
        for (int i = 0; i < n; i++)
        {
            var (x, y) = MapProject.GpsToScreen(region, lat[i], lon[i]);
            if (i == 0) path.MoveTo((float)x, (float)y);
            else path.LineTo((float)x, (float)y);
        }
        canvas.DrawPath(path, line);

        using var marker = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#2fd17a"), Style = SKPaintStyle.Fill };
        var (mx, my) = MapProject.GpsToScreen(region, lat[n - 1], lon[n - 1]);
        canvas.DrawCircle((float)mx, (float)my, 5, marker);
    }
}
```

- [ ] **Step 3: Build** — (from `dotnet/`) `dotnet build` (adapt SkiaSharp API until 0 errors) + `dotnet test` (still green — no new tests this task; build-verified).

- [ ] **Step 4: Commit** — `feat(dotnet): Skia basemap renderer + GPS track overlay`

---

### Task 3: MapWidgetViewModel + SKElement host + wire OVERVIEW (build + live)

**Files:**
- Modify: `TelemetryPoc.App.csproj` (add `SkiaSharp.Views.WPF`)
- Create: `ViewModels/MapWidgetViewModel.cs`, `Views/MapWidgetView.xaml` + `.xaml.cs`
- Modify: `ViewModels/OverviewViewModel.cs`, `Views/OverviewView.xaml`

**Interfaces:**
- Produces: `OverviewViewModel.MapWidget` (`MapWidgetViewModel`); the view hosts an `SKElement`.

- [ ] **Step 1: Add the WPF Skia control** — `cd dotnet && dotnet add src/TelemetryPoc.App package SkiaSharp.Views.WPF --version 2.88.9`.

- [ ] **Step 1b: Add `GpsBounds` to `RideSession.cs`** — compute the whole-ride GPS bbox at `Start` (the map channels are `Widget=="map_lat"/"map_lon"`; their index in `channels` aligns to `Sample.Values`). Add the property + a `using TelemetryPoc.Map;`:

```csharp
public (double MinLat, double MinLon, double MaxLat, double MaxLon)? GpsBounds { get; private set; }
```

In `Start()`, after `var samples = TelemetryDb.LoadSamples(conn, channels);` (and before/after `ApplyMeta`), add:

```csharp
int latIdx = -1, lonIdx = -1;
for (int i = 0; i < channels.Count; i++)
{
    if (channels[i].Widget == "map_lat") latIdx = i;
    if (channels[i].Widget == "map_lon") lonIdx = i;
}
if (latIdx >= 0 && lonIdx >= 0 && samples.Count > 0)
{
    var lat = new double[samples.Count];
    var lon = new double[samples.Count];
    for (int i = 0; i < samples.Count; i++) { lat[i] = samples[i].Values[latIdx]; lon[i] = samples[i].Values[lonIdx]; }
    GpsBounds = TelemetryPoc.Map.MapProject.TrackBounds(lat, lon);
}
```

> This reuses the tested `MapProject.TrackBounds` and makes the static region cover the entire flight from the first paint. (`RideSession` already references `TelemetryPoc.Map`? If not, add the ProjectReference — it was added to App in Phase-1 Task 1, so it's available.)

- [ ] **Step 2: Create `ViewModels/MapWidgetViewModel.cs`** (computes the static Region once; raises Updated):

```csharp
using System;
using System.IO;
using TelemetryPoc.Core;
using TelemetryPoc.Map;

namespace TelemetryPoc.App.ViewModels;

public sealed class MapWidgetViewModel
{
    private readonly RideSession _session;
    public MapWidgetViewModel(RideSession session) { _session = session; }

    public Region? Region { get; private set; }
    public string? MbTilesPath { get; private set; }
    public event Action? Updated;

    public (System.Collections.Generic.IReadOnlyList<double> Lat, System.Collections.Generic.IReadOnlyList<double> Lon) Track
        => _session.Store.GpsTrack();

    /// <summary>Compute the static region from the WHOLE-RIDE GPS bbox once a viewport
    /// size is known and the bounds are available. Waits (does not freeze) until then.</summary>
    public void EnsureRegion(double width, double height)
    {
        if (Region is not null || width < 1 || height < 1) return;
        MbTilesPath ??= ResolveMbTiles();
        var b = _session.GpsBounds;
        if (b is null) return; // bounds set in RideSession.Start; wait rather than freeze wrong
        var (cLat, cLon, z) = TileMath.FitBbox(b.Value.MinLat, b.Value.MinLon, b.Value.MaxLat, b.Value.MaxLon, width, height);
        Region = new Region(cLat, cLon, z, width, height);
    }

    public void Tick() => Updated?.Invoke();

    private static string? ResolveMbTiles()
    {
        var env = Environment.GetEnvironmentVariable("RIDE_MBTILES");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var p = Path.Combine(dir.FullName, "tiles", "israel.mbtiles");
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        return null;
    }
}
```

- [ ] **Step 3: Expose `MapWidget` from `OverviewViewModel.cs`** — add the property + build it in the constructor (it needs no per-tick rebuild) and pump its tick:

```csharp
public MapWidgetViewModel MapWidget { get; }
```

In the constructor, after the existing subscriptions, add:

```csharp
MapWidget = new MapWidgetViewModel(session);
session.Ticked += MapWidget.Tick;
```

(No change to `BuildGroups`/`RefreshRows`; the map VM reads the track live and is driven by its own `Updated`.)

- [ ] **Step 4: Create `Views/MapWidgetView.xaml`**:

```xml
<UserControl x:Class="TelemetryPoc.App.Views.MapWidgetView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:skia="clr-namespace:SkiaSharp.Views.WPF;assembly=SkiaSharp.Views.WPF"
             Background="{StaticResource Panel}">
    <DockPanel>
        <TextBlock DockPanel.Dock="Top" Text="FLIGHT TRACK" Foreground="{StaticResource PanelTitle}"
                   FontFamily="{StaticResource MonoFont}" FontSize="10" Margin="6,4" />
        <skia:SKElement x:Name="Skia" PaintSurface="OnPaintSurface" />
    </DockPanel>
</UserControl>
```

- [ ] **Step 5: Create `Views/MapWidgetView.xaml.cs`** (cache basemap once; overlay per tick):

```csharp
using System.Windows;
using System.Windows.Controls;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using TelemetryPoc.App.ViewModels;
using TelemetryPoc.Map;

namespace TelemetryPoc.App.Views;

public partial class MapWidgetView : UserControl
{
    private MapWidgetViewModel? _vm;
    private SKPicture? _basemap;

    public MapWidgetView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => { if (_vm is null) OnDataContextChanged(this, default); };
        Unloaded += (_, _) => Detach();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();
        _vm = DataContext as MapWidgetViewModel;
        if (_vm is not null) _vm.Updated += OnTick;
    }

    private void Detach()
    {
        if (_vm is not null) _vm.Updated -= OnTick;
        _basemap?.Dispose();
        _basemap = null;
    }

    private void OnTick() => Skia.InvalidateVisual();

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColor.Parse(MapStyle.BackgroundHex));
        if (_vm is null) return;

        var w = e.Info.Width;
        var h = e.Info.Height;
        _vm.EnsureRegion(w, h);
        if (_vm.Region is null) return;

        if (_basemap is null) BuildBasemap(_vm.Region, w, h);
        if (_basemap is not null) canvas.DrawPicture(_basemap);

        var (lat, lon) = _vm.Track;
        TrackOverlay.Draw(canvas, _vm.Region, lat, lon);
    }

    private void BuildBasemap(Region region, int w, int h)
    {
        if (string.IsNullOrEmpty(_vm?.MbTilesPath)) return;
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

> The `Loaded`/`Unloaded` re-subscribe pattern matches `LineChartView` (avoids the tab/virtualization silent-death). `_basemap` is rebuilt lazily; it stays valid because the region is static.

- [ ] **Step 6: Add the map cell to `Views/OverviewView.xaml`** — put it in the line-charts area (row 1) above the charts, or as the first widget. Simplest: wrap the row-1 content so the map sits at the top of the right column's chart area. Replace the `<ScrollViewer Grid.Row="1" ...>` block's inner content to lead with the map. Concretely, change row 1 to a vertical `DockPanel`: the map docked top (fixed height), the charts filling the rest:

```xml
            <DockPanel Grid.Row="1">
                <Border DockPanel.Dock="Top" Height="280" Margin="6" BorderBrush="{StaticResource Border1}" BorderThickness="1">
                    <views:MapWidgetView DataContext="{Binding MapWidget}" />
                </Border>
                <ScrollViewer VerticalScrollBarVisibility="Auto">
                    <ItemsControl ItemsSource="{Binding LineCharts}" Margin="6">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate><WrapPanel Orientation="Horizontal" /></ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border Margin="6" BorderBrush="{StaticResource Border1}" BorderThickness="1">
                                    <views:LineChartView />
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </DockPanel>
```

> `DataContext="{Binding MapWidget}"` points the map view at the `MapWidgetViewModel` (the rest of the column's DataContext is the `OverviewViewModel`). `xmlns:views` is already declared.

- [ ] **Step 7: Build + launch-verify** — (from `dotnet/`) `dotnet build` (fix any SkiaSharp.Views.WPF API mismatch). Then with the real tileset present (`tiles/israel.mbtiles`) and a ride db:
```
RIDE_DB=<abs>/data/ride.db RIDE_MBTILES=<abs>/tiles/israel.mbtiles RIDE_SPEED=5 dotnet run --project src/TelemetryPoc.App
```
Confirm: the map cell shows the **dark INU basemap** (water/roads/buildings) with **street/place labels**, and the **cyan GPS track** drawing on top with a green position marker as the replay advances. Close it. (Controller does the live check.)

- [ ] **Step 8: Full test run** — `dotnet test` → green.

- [ ] **Step 9: Commit** — `feat(dotnet): Skia map widget (SKElement) in OVERVIEW — offline israel.mbtiles + GPS track`

---

## Self-Review

**Spec coverage (Phase 3):** SkiaSharp basemap draw (geometry + opacity + labels) into an `SKPicture` cache (Task 2); GPS track overlay (Task 2); static `Region` computed once from track bbox (Task 1 `TrackBounds`/`FitBbox` + Task 3 `EnsureRegion`); `MapProject.GpsToScreen` projection (Task 1); greedy label collision (Task 1 `LabelLayout`); WPF `SKElement` host with basemap cache + per-tick overlay, mbtiles path resolution, OVERVIEW wiring (Task 3). Pure logic xUnit-tested; Skia/WPF build-verified + live. ✓

**Placeholder scan:** No TBD/TODO. SkiaSharp API is stable 2.88 (the adapt note is a safety valve, full code provided). ✓

**Type consistency:** `StyleLayer(...,Opacity)` consumed by `BasemapRenderer` paint alpha; `MapProject.GpsToScreen/TrackBounds` consumed by `TrackOverlay`/`MapWidgetViewModel`; `LabelBox`/`LabelLayout.Place` consumed by `BasemapRenderer.DrawLabels`; `BasemapRenderer.Render(SKCanvas,Region,MbTilesReader)` + `TrackOverlay.Draw(...)` called by `MapWidgetView`; `MapWidgetViewModel.Region/Track/MbTilesPath/EnsureRegion/Updated` consumed by the view; `OverviewViewModel.MapWidget` bound in XAML. `GpsToScreen(Region(...,400,300), center)=(200,150)` test verified. ✓

**Note:** the basemap is cached once (static region) from the whole-ride GPS bbox (`RideSession.GpsBounds`, computed at `Start` from all samples), so the region covers the entire flight regardless of how far the replay has progressed. `EnsureRegion` waits for `GpsBounds` (set synchronously in `Start`, before the first paint) rather than freezing a wrong region; the cyan track then grows over the fixed basemap each tick.
