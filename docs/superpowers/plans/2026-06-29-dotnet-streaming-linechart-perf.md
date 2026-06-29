# .NET Streaming Line Chart (Perf Fairness) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the .NET line chart update **in place** each frame — append only new samples to a persistent ScottPlot `DataLogger` and let it manage the scrolling window — instead of re-creating a `Scatter` + running `AutoScaleY` over the whole buffer every frame. This removes per-frame plottable allocation so the perf-HUD FPS comparison against the Rust uPlot chart (which mutates in place via `setData`) is fair.

**Architecture:** A pure `StreamTail` helper decides what to append (or whether the series reset) — xUnit-tested. `LineChartView` keeps one `DataLogger` plottable, appends the new tail each `Updated`, and uses the logger's sliding view; no `Clear()`/`Add.Scatter()`/`AutoScaleY()` per frame.

**Tech Stack:** .NET 8 WPF, ScottPlot.WPF 5.0.55, xUnit.

## Global Constraints

- `TelemetryPoc.Core` unchanged. Pure helper in `TelemetryPoc.App.Viz` (xUnit); the ScottPlot view is build-verified + launch-confirmed.
- Behaviour must stay equivalent: a cyan `#38c5e0` line, a relative `m:ss` x-axis, a ~60s scrolling window, dark INU styling. Only the *update mechanism* changes (in-place append, no per-frame re-create).
- The chart must still reset correctly on re-meta (the `ChannelSeries` resets → the on-screen x goes backwards): detect and clear the logger.
- Keep the existing `LineChartViewModel` contract (`XsSeconds`/`Ys`/`WindowMin`/`WindowMax`/`Updated`) — no VM change.

## Existing API (consumed)

- `LineChartViewModel`: `double[] XsSeconds` (elapsed seconds, ascending), `double[] Ys`, `double WindowMin`/`WindowMax`, `event Action? Updated`, `Name`/`Unit`.
- `LineAxis.FormatElapsed(double sec)→string` (the m:ss tick formatter).
- ScottPlot.WPF: `WpfPlot.Plot`, `Plot.Add.DataLogger()` (a realtime append plottable), `WpfPlot.Refresh()`, axis styling.

## File Structure

- `dotnet/src/TelemetryPoc.App.Viz/StreamTail.cs` (new) — `NewFrom`.
- `dotnet/tests/TelemetryPoc.Core.Tests/StreamTailTests.cs` (new).
- `dotnet/src/TelemetryPoc.App/Views/LineChartView.xaml.cs` (modify — DataLogger streaming).

---

### Task 1: StreamTail — what to append / detect reset (pure, xUnit)

**Files:**
- Create: `StreamTail.cs`, `StreamTailTests.cs`

**Interfaces:**
- Produces: `StreamTail.NewFrom(double[] xs, double lastX) → int` — returns:
  - `-1` if the series **reset** (xs non-empty and its last x is `< lastX`, i.e. time went backwards on re-meta) → caller should clear + re-add all;
  - otherwise the **start index** of points with `x > lastX` (the new tail); `xs.Length` if nothing new; `0` if xs empty or `lastX` precedes everything.

- [ ] **Step 1: Write the failing test** — `dotnet/tests/TelemetryPoc.Core.Tests/StreamTailTests.cs`:

```csharp
using TelemetryPoc.App.Viz;
using Xunit;

public class StreamTailTests
{
    [Fact]
    public void Empty_returns_zero()
        => Assert.Equal(0, StreamTail.NewFrom(System.Array.Empty<double>(), 5));

    [Fact]
    public void All_new_when_lastX_precedes()
        => Assert.Equal(0, StreamTail.NewFrom(new[] { 1.0, 2.0, 3.0 }, -1));

    [Fact]
    public void Returns_index_of_first_point_past_lastX()
        => Assert.Equal(2, StreamTail.NewFrom(new[] { 1.0, 2.0, 3.0, 4.0 }, 2.0)); // 3.0,4.0 are new

    [Fact]
    public void Nothing_new_returns_length()
        => Assert.Equal(3, StreamTail.NewFrom(new[] { 1.0, 2.0, 3.0 }, 3.0));

    [Fact]
    public void Reset_returns_minus_one_when_time_went_backwards()
        => Assert.Equal(-1, StreamTail.NewFrom(new[] { 0.5, 1.0 }, 100.0)); // last 1.0 < lastX 100 → reset
}
```

- [ ] **Step 2: Run → fail** — (from `dotnet/`) `dotnet test --filter StreamTailTests`

- [ ] **Step 3: Implement `StreamTail.cs`**:

```csharp
namespace TelemetryPoc.App.Viz;

public static class StreamTail
{
    /// <summary>Index of the first x &gt; lastX (the new tail), xs.Length if none new,
    /// or -1 if the series reset (last x &lt; lastX → time went backwards).</summary>
    public static int NewFrom(double[] xs, double lastX)
    {
        if (xs.Length == 0) return 0;
        if (xs[^1] < lastX) return -1; // re-meta reset
        int i = 0;
        while (i < xs.Length && xs[i] <= lastX) i++;
        return i;
    }
}
```

- [ ] **Step 4: Run → pass** — `dotnet test --filter StreamTailTests`, then full `dotnet test`.

- [ ] **Step 5: Commit** — `feat(dotnet): StreamTail helper for in-place chart streaming (xUnit)`

---

### Task 2: LineChartView — persistent DataLogger streaming (build + live)

**Files:**
- Modify: `dotnet/src/TelemetryPoc.App/Views/LineChartView.xaml.cs`

**Interfaces:**
- Consumes: `StreamTail.NewFrom`, `LineChartViewModel`, ScottPlot `DataLogger`.

- [ ] **Step 1: Rewrite the streaming path** — replace the per-frame `Clear()`+`Add.Scatter()`+`AutoScaleY()` in `LineChartView.xaml.cs` with a persistent `DataLogger`. Add fields and rework `StylePlot`/`Redraw`/`Detach`:

```csharp
// fields
private ScottPlot.Plottables.DataLogger? _logger;
private double _lastX = double.NegativeInfinity;
```

In `StylePlot()` (after the existing dark styling, before `Plot.Refresh()`), create the logger once:

```csharp
        _logger = p.Add.DataLogger();
        _logger.Color = Color.FromHex("#38c5e0");
        _logger.LineWidth = 1.5f;
        _logger.ManageAxisLimits = false; // we drive the X window; Y autoscales within the view
```

Replace `Redraw()` with an append-only version:

```csharp
    private void Redraw()
    {
        if (_vm is null || _logger is null) return;
        var xs = _vm.XsSeconds;
        var ys = _vm.Ys;

        var start = StreamTail.NewFrom(xs, _lastX);
        if (start == -1) // series reset (re-meta) → clear and re-add
        {
            _logger.Clear();
            _lastX = double.NegativeInfinity;
            start = 0;
        }
        for (int i = start; i < xs.Length && i < ys.Length; i++)
            _logger.Add(xs[i], ys[i]);
        if (xs.Length > 0) _lastX = xs[^1];

        Plot.Plot.Axes.SetLimitsX(_vm.WindowMin, _vm.WindowMax);
        Plot.Plot.Axes.AutoScaleY();
        Plot.Refresh();
    }
```

> **ScottPlot 5.0.55 API note:** `Plot.Add.DataLogger()`, `DataLogger.Add(double x, double y)`, `.Clear()`, `.Color`, `.LineWidth`, `.ManageAxisLimits` are the ScottPlot 5 DataLogger surface. If a member differs in 5.0.55, **adapt** (inspect `ScottPlot.Plottables.DataLogger`) until `dotnet build` is clean — e.g. the color/width may be under `.LineStyle`/`.Line`, and the per-frame `AutoScaleY` may be replaced by the logger's own managed view (`ManageAxisLimits = true` + `ViewSlide`). The intent: **append the new tail only** (no per-frame plottable re-creation), keep the m:ss axis + scroll window. Record any deviation.

> Note: per-frame `AutoScaleY()` is kept (cheap relative to re-creating the plottable; it's a Y-only rescale). The big win is dropping the per-frame `Clear()` + `Add.Scatter()` allocation. If `DataLogger` exposes a managed sliding view that also handles Y, prefer it and drop the manual `AutoScaleY`.

In `Detach()`, reset the stream state so a re-attached view restarts cleanly. Add after the existing detach lines:

```csharp
        _logger?.Clear();
        _lastX = double.NegativeInfinity;
```

(Keep `_logger` itself — it belongs to the Plot, recreated only if the control is recreated. If `Detach` runs on Unloaded and the same control reloads, `StylePlot` is NOT re-run, so the existing `_logger` is reused; clearing it + resetting `_lastX` makes the next `Redraw` re-add from scratch. That is correct.)

- [ ] **Step 2: Build** — (from `dotnet/`) `dotnet build` (adapt the DataLogger API until 0 errors) + `dotnet test` (110 green; the new `StreamTailTests` from Task 1 included).

- [ ] **Step 3: Launch-verify (perf)** — run the app:
```
RIDE_DB=<abs>/data/ride.db RIDE_MBTILES=<abs>/tiles/israel.mbtiles RIDE_SPEED=5 dotnet run --project src/TelemetryPoc.App
```
Confirm the line charts still draw a **cyan scrolling line with the m:ss axis** (visually unchanged), and the **HUD FPS is higher / frame time lower** than before (the per-frame Scatter re-creation is gone). Close it. (Controller does the live check.)

- [ ] **Step 4: Commit** — `perf(dotnet): stream line chart via persistent DataLogger (no per-frame Scatter re-create)`

---

## Self-Review

**Spec coverage:** in-place streaming via a persistent `DataLogger` fed only the new tail (Task 2), with reset detection (`StreamTail.NewFrom` → -1, Task 1); behaviour unchanged (cyan line, m:ss axis, 60s window); no `LineChartViewModel` change. ✓

**Placeholder scan:** No TBD/TODO. The DataLogger API note is an adapt-on-build instruction (ScottPlot matched the plan exactly in prior phases), with full code. ✓

**Type consistency:** `StreamTail.NewFrom(double[], double)→int` consumed by `LineChartView.Redraw`; `_lastX` tracks the last appended x; the VM contract (`XsSeconds`/`Ys`/`WindowMin`/`WindowMax`/`Updated`) is unchanged. Test value `NewFrom([1,2,3,4],2.0)=2` verified. ✓

**Note:** `DataLogger` retains all appended points (memory grows with the ride: ~6k points for a 10-min ride, fine; a 12h ride would accumulate ~432k — acceptable for the PoC, and the sliding X window still shows only ~60s). If this matters later, switch to a fixed-capacity `DataStreamer`.
