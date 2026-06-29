# .NET WPF INU Reskin — Phase 4 (ScottPlot.WPF Line Charts) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add live **strip-chart** widgets to the OVERVIEW screen — a ScottPlot.WPF realtime time-series per `widget=="strip"` channel, with a 60-second scrolling window, a relative `m:ss` x-axis, auto-ranged y, the cyan accent, and dark INU styling — mirroring the Rust uPlot line widgets.

**Architecture:** The data prep (ms→s, scroll-window math) and axis label formatting live in `TelemetryPoc.App.Viz` (pure, xUnit-tested), mirroring the Rust `uplotData.ts`. A `LineChartViewModel` reads the channel's `ChannelSeries` each tick and raises an `Updated` event; `LineChartView` hosts a `ScottPlot.WPF.WpfPlot`, redraws on `Updated` (push xs/ys, set the X window, autoscale Y, format ticks), styled dark once at init. No interactions (perf-focus).

**Tech Stack:** .NET 8 WPF, **ScottPlot.WPF 5.0.55** (already referenced), xUnit, `TelemetryPoc.Core`.

## Global Constraints

- `TelemetryPoc.Core` stays **unchanged**.
- Pure logic (scroll window, ms→s, `m:ss` formatting) in `TelemetryPoc.App.Viz` + xUnit; ScottPlot/WPF views build-verified + launch-confirmed.
- UI updates on the WPF UI thread (`RideSession` DispatcherTimer / `Ticked`).
- Window = 60 000 ms. Scroll-window math mirrors `rust/src/ui/app/widgets/uplotData.ts` EXACTLY: empty → `(0, windowMs/1000)`; else `min = max(first, last - windowMs)`, return `(min/1000, last/1000)` (anchors the left edge to the first sample until the span exceeds the window, then scrolls). `FormatElapsed(sec) = "{m}:{ss}"` with `m = floor(s/60)`, `ss = (floor(s)%60)` zero-padded to 2, negatives clamp to 0.
- INU palette: figure/panel bg `#10151d`; data bg `#0c121a`; grid `#1d2632`; axis text `#566273`; line accent `#38c5e0`.
- Charts auto-range y, no animation, no interaction. Same eviction/window as Rust (Core `ChannelSeries` 60 s window) so render load is comparable.

## Existing API (consumed)

- `TelemetryStore`: `Channels` (`ChannelMeta.Widget` — strip channels `=="strip"`), `Series(long)→ChannelSeries?`.
- `ChannelSeries.Arrays()→(IReadOnlyList<long> Xs, IReadOnlyList<double> Ys)`; `Xs` are **ms from ride start**.
- `RideSession`: `Store`, `MetaLoaded`/`Ticked`, `Start()`.
- `OverviewViewModel(RideSession)`: builds on `MetaLoaded` (`BuildGroups`), refreshes on `Ticked` (`RefreshRows`); already exposes `Groups` + `Gauges`.

## File Structure

- `dotnet/src/TelemetryPoc.App.Viz/LineData.cs` (new) — `ToSeconds`, `ScrollWindow`.
- `dotnet/src/TelemetryPoc.App.Viz/LineAxis.cs` (new) — `FormatElapsed`.
- `dotnet/tests/TelemetryPoc.Core.Tests/LineDataTests.cs`, `LineAxisTests.cs` (new).
- `dotnet/src/TelemetryPoc.App/ViewModels/LineChartViewModel.cs` (new).
- `dotnet/src/TelemetryPoc.App/ViewModels/OverviewViewModel.cs` (modify — `LineCharts`).
- `dotnet/src/TelemetryPoc.App/Views/LineChartView.xaml` + `.xaml.cs` (new — hosts WpfPlot).
- `dotnet/src/TelemetryPoc.App/Views/OverviewView.xaml` (modify — gauges row + charts area).

---

### Task 1: Viz scroll-window + ms→s + elapsed-axis formatting (pure, xUnit)

**Files:**
- Create: `LineData.cs`, `LineAxis.cs`, `LineDataTests.cs`, `LineAxisTests.cs`

**Interfaces:**
- Produces:
  - `LineData.ToSeconds(IReadOnlyList<long> xsMs) → double[]`
  - `LineData.ScrollWindow(IReadOnlyList<long> xsMs, long windowMs) → (double Min, double Max)`
  - `LineAxis.FormatElapsed(double sec) → string`

- [ ] **Step 1: Write the failing tests** — `LineDataTests.cs`:

```csharp
using TelemetryPoc.App.Viz;
using Xunit;

public class LineDataTests
{
    [Fact]
    public void ToSeconds_divides_by_1000()
        => Assert.Equal(new[] { 0.0, 1.0, 2.0 }, LineData.ToSeconds(new long[] { 0, 1000, 2000 }));

    [Fact]
    public void ToSeconds_empty() => Assert.Empty(LineData.ToSeconds(System.Array.Empty<long>()));

    [Fact]
    public void ScrollWindow_empty_is_zero_to_window_seconds()
        => Assert.Equal((0.0, 60.0), LineData.ScrollWindow(System.Array.Empty<long>(), 60_000));

    [Fact]
    public void ScrollWindow_scrolls_once_span_exceeds_window()
    {
        // last=10000, window=4000 → min=max(0, 6000)=6000 → (6,10)
        Assert.Equal((6.0, 10.0), LineData.ScrollWindow(new long[] { 0, 5000, 10000 }, 4000));
    }

    [Fact]
    public void ScrollWindow_anchors_left_while_shorter_than_window()
    {
        // 5.3s of data in a 60s window → (0, 5.3), not pinned right
        Assert.Equal((0.0, 5.3), LineData.ScrollWindow(new long[] { 0, 1000, 5300 }, 60_000));
    }
}
```

`LineAxisTests.cs`:

```csharp
using TelemetryPoc.App.Viz;
using Xunit;

public class LineAxisTests
{
    [Theory]
    [InlineData(0.0, "0:00")]
    [InlineData(5.0, "0:05")]
    [InlineData(70.0, "1:10")]
    [InlineData(600.0, "10:00")]
    [InlineData(12.9, "0:12")]
    [InlineData(-3.0, "0:00")]
    public void FormatElapsed_is_m_ss(double sec, string s) => Assert.Equal(s, LineAxis.FormatElapsed(sec));
}
```

- [ ] **Step 2: Run → fail** — (from `dotnet/`) `dotnet test --filter "LineDataTests|LineAxisTests"`

- [ ] **Step 3: Implement `LineData.cs`**:

```csharp
namespace TelemetryPoc.App.Viz;

public static class LineData
{
    public static double[] ToSeconds(IReadOnlyList<long> xsMs)
    {
        var a = new double[xsMs.Count];
        for (int i = 0; i < a.Length; i++) a[i] = xsMs[i] / 1000.0;
        return a;
    }

    public static (double Min, double Max) ScrollWindow(IReadOnlyList<long> xsMs, long windowMs)
    {
        var w = Math.Max(1, windowMs);
        if (xsMs.Count == 0) return (0, w / 1000.0);
        var last = xsMs[^1];
        var first = xsMs[0];
        var min = Math.Max(first, last - w);
        return (min / 1000.0, last / 1000.0);
    }
}
```

- [ ] **Step 4: Implement `LineAxis.cs`**:

```csharp
namespace TelemetryPoc.App.Viz;

public static class LineAxis
{
    public static string FormatElapsed(double sec)
    {
        var s = (long)Math.Max(0, Math.Floor(sec));
        var m = s / 60;
        var r = s % 60;
        return m + ":" + r.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
    }
}
```

- [ ] **Step 5: Run → pass** — `dotnet test --filter "LineDataTests|LineAxisTests"`, then full `dotnet test`.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(dotnet): App.Viz line-chart data + elapsed axis formatting (xUnit)"
```

---

### Task 2: LineChartViewModel + OverviewViewModel.LineCharts

**Files:**
- Create: `ViewModels/LineChartViewModel.cs`
- Modify: `ViewModels/OverviewViewModel.cs`

**Interfaces:**
- Consumes: `LineData.ToSeconds`/`ScrollWindow`, `RideSession`/store.
- Produces:
  - `LineChartViewModel(ChannelMeta)` with `Name`/`Unit` (string), `XsSeconds`/`Ys` (`double[]`), `WindowMin`/`WindowMax` (double), `event Action? Updated`, `Refresh(TelemetryStore)`.
  - `OverviewViewModel.LineCharts` (`ObservableCollection<LineChartViewModel>`), built on MetaLoaded + refreshed on Ticked.

- [ ] **Step 1: Create `ViewModels/LineChartViewModel.cs`**:

```csharp
using TelemetryPoc.App.Viz;
using TelemetryPoc.Core;

namespace TelemetryPoc.App.ViewModels;

public sealed class LineChartViewModel
{
    private const long WindowMs = 60_000;
    private readonly ChannelMeta _ch;
    public LineChartViewModel(ChannelMeta ch) { _ch = ch; }

    public string Name => _ch.Name;
    public string Unit => _ch.Unit;

    public double[] XsSeconds { get; private set; } = Array.Empty<double>();
    public double[] Ys { get; private set; } = Array.Empty<double>();
    public double WindowMin { get; private set; }
    public double WindowMax { get; private set; }

    public event Action? Updated;

    public void Refresh(TelemetryStore store)
    {
        var (xs, ys) = store.Series(_ch.Id)?.Arrays() ?? ((IReadOnlyList<long>)Array.Empty<long>(), (IReadOnlyList<double>)Array.Empty<double>());
        XsSeconds = LineData.ToSeconds(xs);
        var yy = new double[ys.Count];
        for (int i = 0; i < yy.Length; i++) yy[i] = ys[i];
        Ys = yy;
        var (min, max) = LineData.ScrollWindow(xs, WindowMs);
        WindowMin = min; WindowMax = max;
        Updated?.Invoke();
    }
}
```

- [ ] **Step 2: Add `LineCharts` to `OverviewViewModel.cs`** — add the collection, build it in `BuildGroups`, refresh it in `RefreshRows` (alongside `Gauges`). Add property:

```csharp
public ObservableCollection<LineChartViewModel> LineCharts { get; } = new();
```

In `BuildGroups()`, after the `Gauges` build block and before `RefreshRows()`, add:

```csharp
LineCharts.Clear();
foreach (var ch in store.Channels)
    if (ch.Widget == "strip") LineCharts.Add(new LineChartViewModel(ch));
```

In `RefreshRows()`, after the `Gauges` refresh loop, add:

```csharp
foreach (var lc in LineCharts) lc.Refresh(_session.Store);
```

- [ ] **Step 3: Build + test** — (from `dotnet/`) `dotnet build` (0 errors) + `dotnet test` (still green; no new unit tests — VM is build-verified, logic covered by Task 1).

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(dotnet): LineChartViewModel + OverviewViewModel.LineCharts"
```

---

### Task 3: LineChartView (ScottPlot.WPF) + host in OVERVIEW

**Files:**
- Create: `Views/LineChartView.xaml` + `.xaml.cs`
- Modify: `Views/OverviewView.xaml`

**Interfaces:**
- Consumes: `LineChartViewModel` (DataContext: `Name`/`Unit`/`XsSeconds`/`Ys`/`WindowMin`/`WindowMax`/`Updated`).

> **ScottPlot 5.0.55 API note:** the calls below are the ScottPlot 5 surface. If a member name differs in 5.0.55, the **build will fail** — adapt to the exact name (e.g. `Axes.AutoScaleY()` vs `Axes.AutoScale()`, `Axes.Color(...)`, the `NumericAutomatic.LabelFormatter` property) and record the change in your report. The shape (add scatter → set X limits → autoscale Y → format ticks → Refresh) stays the same.

- [ ] **Step 1: Create `Views/LineChartView.xaml`** (header + WpfPlot):

```xml
<UserControl x:Class="TelemetryPoc.App.Views.LineChartView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:sp="clr-namespace:ScottPlot.WPF;assembly=ScottPlot.WPF"
             Width="380" Height="190" Background="{StaticResource Panel}">
    <DockPanel Margin="4">
        <Grid DockPanel.Dock="Top" Margin="4,2">
            <TextBlock Text="{Binding Name}" Foreground="{StaticResource PanelTitle}"
                       FontFamily="{StaticResource MonoFont}" FontSize="10" />
            <TextBlock Text="{Binding Unit}" HorizontalAlignment="Right" Foreground="{StaticResource TextDim}"
                       FontFamily="{StaticResource MonoFont}" FontSize="9" />
        </Grid>
        <sp:WpfPlot x:Name="Plot" />
    </DockPanel>
</UserControl>
```

- [ ] **Step 2: Create `Views/LineChartView.xaml.cs`** — style once, redraw on the VM's `Updated`:

```csharp
using System.Windows;
using System.Windows.Controls;
using ScottPlot;
using TelemetryPoc.App.ViewModels;

namespace TelemetryPoc.App.Views;

public partial class LineChartView : UserControl
{
    private LineChartViewModel? _vm;

    public LineChartView()
    {
        InitializeComponent();
        StylePlot();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => Detach();
    }

    private void StylePlot()
    {
        var p = Plot.Plot;
        p.FigureBackground.Color = Color.FromHex("#10151d");
        p.DataBackground.Color = Color.FromHex("#0c121a");
        p.Axes.Color(Color.FromHex("#566273"));
        p.Grid.MajorLineColor = Color.FromHex("#1d2632");
        // relative m:ss x-axis labels
        if (p.Axes.Bottom.TickGenerator is ScottPlot.TickGenerators.NumericAutomatic gen)
            gen.LabelFormatter = x => LineAxis.FormatElapsed(x);
        Plot.Refresh();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();
        _vm = DataContext as LineChartViewModel;
        if (_vm is not null)
        {
            _vm.Updated += Redraw;
            Redraw();
        }
    }

    private void Detach()
    {
        if (_vm is not null) _vm.Updated -= Redraw;
        _vm = null;
    }

    private void Redraw()
    {
        if (_vm is null) return;
        var p = Plot.Plot;
        p.Clear();
        if (_vm.XsSeconds.Length > 0)
        {
            var sc = p.Add.Scatter(_vm.XsSeconds, _vm.Ys);
            sc.Color = Color.FromHex("#38c5e0");
            sc.LineWidth = 1.5f;
            sc.MarkerSize = 0;
            p.Axes.AutoScaleY();
            p.Axes.SetLimitsX(_vm.WindowMin, _vm.WindowMax);
        }
        Plot.Refresh();
    }
}
```

> `using TelemetryPoc.App.Viz;` is needed for `LineAxis` — add it if your IDE doesn't. `Color` is `ScottPlot.Color`.

- [ ] **Step 3: Restructure the OVERVIEW right column in `Views/OverviewView.xaml`** — split column 1 into a gauges row (top, auto) + a line-charts area (fill). Replace the single gauges `ItemsControl` (Grid.Column="1") with:

```xml
        <Grid Grid.Column="1">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
            </Grid.RowDefinitions>
            <ItemsControl Grid.Row="0" ItemsSource="{Binding Gauges}" Margin="6">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate><WrapPanel Orientation="Horizontal" /></ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Margin="6" BorderBrush="{StaticResource Border1}" BorderThickness="1">
                            <views:GaugeView />
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
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
        </Grid>
```

> Each `LineChartView`'s DataContext is the `LineChartViewModel` item (set by the ItemTemplate). `xmlns:views` is already declared.

- [ ] **Step 4: Build + launch-verify** — (from `dotnet/`) `dotnet build` (fix any ScottPlot API mismatch until 0 errors); `dotnet run --project src/TelemetryPoc.App`. Confirm: below the gauges, one strip chart per `strip` channel, each drawing a **cyan line** that **scrolls** with a relative `m:ss` x-axis (after ~60 s the window scrolls; the trace fills the width before that), dark styling, auto-ranged y. Close it. (Controller does the live check.)

- [ ] **Step 5: Full test run** — `dotnet test` → green.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(dotnet): ScottPlot.WPF strip charts in OVERVIEW (60s scroll, m:ss axis)"
```

---

## Self-Review

**Spec coverage (Phase 4):** ScottPlot.WPF realtime strip charts (Task 3); 60s scrolling window + relative `m:ss` axis + ms→s mirroring uplotData.ts (Task 1); per-`strip`-channel VMs refreshed on tick (Task 2); dark INU styling + cyan accent (Task 3). ✓

**Placeholder scan:** No TBD/TODO. The ScottPlot API note is an explicit adapt-on-build instruction (the package is pinned 5.0.55), not a gap — full code is provided. ✓

**Type consistency:** `LineData.ToSeconds`/`ScrollWindow`, `LineAxis.FormatElapsed`, `LineChartViewModel` (`XsSeconds`/`Ys`/`WindowMin`/`WindowMax`/`Updated`/`Refresh`), `OverviewViewModel.LineCharts` — consistent across Tasks 1→3. `ChannelSeries.Arrays()` tuple shape matches the VM's destructuring. ScrollWindow test `(0,5000,10000)/4000 → (6,10)` matches the mirrored Rust formula. ✓

**Note:** `OverviewViewModel` now builds/refreshes three widget collections (`Groups`, `Gauges`, `LineCharts`) on the same MetaLoaded/Ticked events — keep the existing two intact when adding the third.
