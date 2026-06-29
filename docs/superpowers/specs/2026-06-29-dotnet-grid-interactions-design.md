# .NET WPF Grid Interactions — Design

**Date:** 2026-06-29
**Status:** Approved (brainstorm)
**Mirrors:** Rust dashboard Phase 4 (PR #12) — `rust/src/ui/app/widgets/*`

## Goal

Give the native WPF dashboard the same interactive widget grid the Rust app has: a single unified
snap-to-grid surface where the map, gauges, and charts are all free-floating cells the user can
create, move, resize, reorder, remove, and reconfigure. Plus line-chart zoom/hover and interactive
map pan/zoom. The deliverable keeps the two stacks behaviorally equivalent so the perf comparison
stays fair.

## Current state (what we replace)

`OverviewView.xaml` has **three fixed regions**: gauges in a top `WrapPanel`, a fixed 280px map,
charts in a bottom `WrapPanel`. Widgets live in three separate `ObservableCollection`s
(`Gauges`, `LineCharts`, single `MapWidget`) with no position/size model and no interaction.

The Rust app instead has **one CSS grid** (158px cells, 10px gap → pitch 168) where every widget is a
cell with `{col, row, cols, rows}`. We collapse the .NET layout to match.

## Reference: Rust behavior to mirror (exact values)

| Concept | Value | Source |
|---|---|---|
| Cell size | 158px, gap 10px, **pitch 168** | `theme.css` `.widgetgrid`, `dropGrid.ts` PITCH |
| Resize deadzone | 144px (`pitch − 24`) | `dropGrid.ts` `resizeStep` |
| Gauge constraint | 1–6 cols/rows, **always square** (`max(cols,rows)`) | `widgetModel.ts` |
| Line constraint | 1–6 cols, 1–4 rows | `widgetModel.ts` |
| Map constraint | 2–8 cols, 2–6 rows | `widgetModel.ts` |
| Drag-create default | **gauge, 1×1**, at snapped cell | `widgetModel.ts` `add` |
| Toggle gauge→line | `cols = max(2, cols)`, `rows = max(1, rows)` | `widgetModel.ts` `toggle` |
| Toggle line→gauge | `1×1` | `widgetModel.ts` `toggle` |
| Line zoom | ×2 / ÷2, **clamp [1,8]**; window = `60000 / zoom` ms | `widgetModel.ts`, `LineChart.tsx` |
| Seed: map | 4×4 if both `map_lat`+`map_lon` channels exist | `layout.ts` |
| Seed: gauges | 1×1 per `widget=="gauge"` channel, by `display_order` | `layout.ts` |
| Seed: lines | 2×1 per `widget=="strip"` channel | `layout.ts` |
| Collision | **none — overlaps allowed**, no auto-reflow | `widgetModel.ts` (no collision code) |
| Persistence | **in-memory only**, resets on relaunch | `useWidgets.ts` |
| Map initial | fit track bbox (already in .NET via `TileMath.FitBbox`) | `MapWidget.tsx` `fitTrack` |

> Note: Rust seeds lines only for `roll`/`acc_z`; .NET already builds a line per `strip` channel in
> `OverviewViewModel.BuildGroups`. We keep .NET's existing "line per strip channel" seed (it is the
> closer mirror of the *data contract*, and the user already sees those charts today).

## Architecture

Three layers, following the repo's existing split (pure logic is xUnit-tested; XAML/Skia/ScottPlot
are build-verified + live-launch confirmed).

### 1. Pure logic — `TelemetryPoc.App.Viz` (new `WidgetLayout.cs`)

The grid state machine and geometry, no WPF. Ported from `widgetModel.ts` + `dropGrid.ts`.

```csharp
public enum WidgetKind { Gauge, Line, Map }

public sealed record Widget(
    string Id, WidgetKind Kind, int? ChannelId,
    string Name, string Unit,
    int Col, int Row, int Cols, int Rows, int Zoom);

public static class WidgetLayout
{
    public const int Pitch = 168;
    public const int CellSize = 158;
    public const int Gap = 10;
    public const int Deadzone = 144;   // pitch - 24
    public const int ZoomMin = 1, ZoomMax = 8;

    // dropGrid.ts cellFromPoint — 1-indexed, min 1
    public static (int Col, int Row) CellFromPoint(double x, double y, double scrollX, double scrollY);
    // dropGrid.ts resizeStep — pixel delta → grid-cell delta with deadzone
    public static int ResizeStep(double deltaPx);
    // widgetModel.ts clampSize — per-kind min/max + gauge-square
    public static (int Cols, int Rows) ClampSize(WidgetKind kind, int cols, int rows);
    // widgetModel.ts toggle — returns new (kind, cols, rows)
    public static (WidgetKind Kind, int Cols, int Rows) Toggle(WidgetKind kind, int cols, int rows);
    public static int ZoomBy(int zoom, double factor);   // clamp [1,8]
    // widgetModel.ts firstFit packing for the seed layout
    public static (int Col, int Row) FirstFit(IEnumerable<Widget> placed, int cols, int rows, int seedCols = 8);
}
```

`SeedLayout(channels)` (in `WidgetLayout` or a sibling `WidgetSeed.cs`) returns the initial
`IReadOnlyList<Widget>`: map 4×4 (if lat+lon), gauge 1×1 per gauge channel by display order, line 2×1
per strip channel — each placed via `FirstFit` in an 8-col virtual grid.

### 2. State — `TelemetryPoc.App` (new `DashboardViewModel`)

Holds `ObservableCollection<WidgetViewModel>`, built from `SeedLayout` on `MetaLoaded`. In-memory
only. Public ops delegate the math to `WidgetLayout`, then mutate the affected `WidgetViewModel`:

- `AddFromChannel(channelId, name, unit, col, row)` → new gauge 1×1 (id `w-{next}`).
- `Move(id, col, row)`, `Resize(id, cols, rows)`, `Toggle(id)`, `Remove(id)`, `ZoomBy(id, factor)`.

`WidgetViewModel` wraps the existing content VMs (`GaugeViewModel` / `LineChartViewModel` /
`MapWidgetViewModel`) plus layout props (`Col,Row,Cols,Rows` + computed `Left,Top,Width,Height` in
DIPs) and `Kind`. Per-frame `Refresh(store)` forwards to the inner content VM (unchanged data path).

### 3. View — `TelemetryPoc.App` (new `WidgetGridView`)

Replaces the 3-region body of `OverviewView` (the PARAMETERS sidebar stays). An `ItemsControl` over
`Widgets` with a `Canvas` `ItemsPanel`; each item's `Canvas.Left/Top` + `Width/Height` bind to the
VM's computed DIPs. Item template = a cell `Border` with:

- **Header:** `☰ {name}` grip (drag-move), a `LINE`/`GAUGE` toggle button, a red `×` (gauge/line only).
- **Body:** a `ContentControl` whose template is chosen by `Kind` → existing `GaugeView` /
  `LineChartView` / `MapWidgetView`.
- **Resize handle:** a bottom-right corner `Thumb`/grip (`nwse-resize`).

## Interactions (WPF mechanics)

- **Param → create:** `ParamPanel` rows become drag sources (`DragDrop.DoDragDrop`, payload
  `channelId/name/unit`). The grid `Canvas` is the drop target; on drop, `CellFromPoint` → `AddFromChannel`.
- **Move:** header grip `MouseDown` captures the mouse; on `MouseUp`, `CellFromPoint` → `Move`.
- **Resize:** corner thumb captures; `DragDelta` → `ResizeStep` on accumulated delta → `Resize`.
- **Toggle / Remove:** button clicks → `Toggle(id)` / `Remove(id)`.

## Line chart zoom + hover

- `LineChartViewModel` gains `Zoom` (1–8). `WindowMs = 60000 / Zoom` (replaces the const). Methods
  `ZoomBy(factor)` / `ResetZoom()` clamp via `WidgetLayout.ZoomBy`.
- The line cell gets a WPF `ContextMenu` (right-click, mirroring Rust `LineMenu`): **Zoom in** (×2),
  **Zoom out** (÷2), **Reset**. ScottPlot's built-in mouse zoom/pan is **disabled** so the
  window-based model is the only zoom (keeps parity + keeps perf comparable).
- **Hover:** `LineChartView` handles `MouseMove`; ScottPlot maps pixel→data; a pure
  `NearestSample(xs, ys, xData)` helper (App.Viz, testable) finds the closest point; a marker +
  floating `TextBlock` shows `"{m:ss} · {value} {unit}"` (mirrors `linechart-tip`). Leaving the plot
  hides it.

## Map pan + zoom (custom Skia)

`Region(CenterLat, CenterLon, Zoom, Width, Height)` already exists; we make it interactive.

- **New pure math** in `TelemetryPoc.Map` (xUnit):
  - `WebMercator.WorldToLonLat(worldX, worldY, z)` — inverse of `LonLatToWorld`.
  - `MapInteract.Pan(Region, dxPx, dyPx)` → new `Region` with center shifted by the pixel delta
    (converted through world coords at the current zoom).
  - `MapInteract.ZoomAt(Region, cursorX, cursorY, step, minZoom, maxZoom)` → new `Region` that zooms
    one integer level toward the cursor (MapLibre parity), clamped to the MBTiles zoom range.
- **Zoom range:** the MBTiles min/max zoom; default `[9, 14]` (the `TileMath.FitBbox` loop range).
- **`MapWidgetView.xaml.cs`:** add `MouseWheel` (→ `ZoomAt`), `MouseDown/Move/Up` (→ `Pan`, with
  capture), and double-click (→ re-fit via `TileMath.FitBbox`, the current behavior as "recenter").
  On any `Region` change: rebuild the basemap `SKPicture` at the new zoom and `InvalidateVisual`.

## Out of scope (YAGNI)

- Layout persistence (Rust has none).
- Collision detection / auto-reflow (Rust allows overlaps).
- Touch / multi-touch gestures.
- Creating map or line widgets by drag (Rust drag-create makes a gauge only; param rows are
  gauge/strip channels).
- Live speed control, animation easing on pan/zoom.

## Testing strategy

- **xUnit (pure logic):** `WidgetLayout` (CellFromPoint, ResizeStep, ClampSize, Toggle, ZoomBy,
  FirstFit), `SeedLayout`, `NearestSample`, `WebMercator.WorldToLonLat` round-trip, `MapInteract.Pan`,
  `MapInteract.ZoomAt`. These carry the correctness load.
- **Build-verify + live launch:** `WidgetGridView`, drag/move/resize/toggle/remove handlers,
  ScottPlot context menu + hover, map mouse handlers. Visual correctness checked against
  `docs/reference/dashboard-target.md` and the Rust app.

## Risks

- **Map basemap rebuild cost** on every pan/zoom (tile decode). Mitigation: the `MBTilesReader`
  already returns per-tile data; cache decoded tiles by `(z,x,y)` if a live launch shows jank.
- **ScottPlot default interactions** may fight the custom zoom if not fully disabled — verify on launch.
- **Drag-and-drop vs. mouse-capture** conflicts (param OLE drag vs. widget move): scope the OLE drag
  to `ParamPanel` rows only; widget move/resize use plain mouse capture inside the cell.
