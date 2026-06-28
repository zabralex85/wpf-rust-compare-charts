# .NET WPF INU Reskin — Phase 3 (Gauges + Transport Footer) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fill the OVERVIEW right column with live **radial gauge** widgets (SkyPitch, SkyRoll) and add the bottom **transport footer** (read-only clock / T+ / progress bar / BUFFER·SAMPLES·DROPPED), matching the INU dashboard.

**Architecture:** The gauge math (auto-scale + needle angle + scale labels) and number/clock formatting live in `TelemetryPoc.App.Viz` (pure, xUnit-tested), mirroring the Rust `gaugeViz.ts`. A WPF `GaugeView` draws the dial/ticks/needle on a Canvas, rotating the needle by the computed angle. `GaugeViewModel` refreshes per tick. The transport footer reads ride progress + buffered-sample count from `RideSession`/store on each tick. No interactions (perf-focus).

**Tech Stack:** .NET 8 WPF, xUnit, `TelemetryPoc.Core`.

## Global Constraints

- `TelemetryPoc.Core` stays **unchanged**.
- Pure logic (gauge math, formatting, short clock) in `TelemetryPoc.App.Viz` + xUnit; WPF views/XAML build-verified + launch-confirmed.
- UI updates on the WPF UI thread (`RideSession` DispatcherTimer / `Ticked` event).
- Gauge math mirrors `rust/src/ui/app/widgets/gaugeViz.ts` EXACTLY: auto-scale `raw = max(|v|*1.3, 1e-6)`, `ex = floor(log10(raw))`, `ff = raw/10^ex`, nice `nf ∈ {1 (ff≤1), 2 (≤2), 2.5 (≤2.5), 5 (≤5), 10 (else)}`, `R = nf*10^ex`; needle `frac = clamp((v+R)/(2R), 0, 1)`, `angleDeg = -135 + frac*270`; scale labels `fmtScale(-R), fmtScale(-R/2), fmtScale(R/2), fmtScale(R)`; value `fmtNum(v)`.
- `fmtNum`: not-finite→"—"; `|v|≥100`→F1; `≥1`→F3; else F6. `fmtScale`: 0→"0"; `≥100`→F0; `≥1`→F1; `≥0.1`→F2; else F3; then strip trailing zeros + trailing ".". `fmtGaugeValue`: not-finite→"—"; `|v|≥100`→F1 else F3. (Invariant culture.)
- INU palette: accent cyan `#38c5e0`; green `#2fd17a`; value text `#c3ccd8`; dim `#566273`; panel `#10151d`; panel2 `#0c121a`; border `#1d2632`; panel-title `#8b98a9`.
- Transport footer is **read-only** — transport control glyphs are decorative; no pause/seek wired.

## Existing API (consumed)

- `TelemetryStore`: `Channels` (`IReadOnlyList<ChannelMeta>`), `Latest(long)→double?`, `Series(long)→ChannelSeries?`; `ChannelSeries.Len`.
- `ChannelMeta(... string Widget ...)` — gauge channels have `Widget=="gauge"`, strip channels `Widget=="strip"`.
- `TelemetryDb.LoadRideMeta(conn)→RideMeta(long StartTime, long DurationS, long RateHz, long ChannelCount)`.
- `RideSession` (Phase 2): `TelemetryStore Store`, `string ClockText`/`TPlusText`, `MetaLoaded`/`Ticked` events, `Start()`.

## File Structure

- `dotnet/src/TelemetryPoc.App.Viz/GaugeFormat.cs` (new) — `Num`/`Scale`/`GaugeValue`.
- `dotnet/src/TelemetryPoc.App.Viz/GaugeViz.cs` (new) — `Compute(value)→GaugeVizResult`.
- `dotnet/src/TelemetryPoc.App.Viz/MissionClock.cs` (modify) — add `FormatShort`.
- `dotnet/tests/TelemetryPoc.Core.Tests/GaugeFormatTests.cs`, `GaugeVizTests.cs` (new); `MissionClockTests.cs` (modify).
- `dotnet/src/TelemetryPoc.App/ViewModels/GaugeViewModel.cs`, `TransportViewModel.cs` (new); `OverviewViewModel.cs` (modify — expose `Gauges`).
- `dotnet/src/TelemetryPoc.App/Views/GaugeView.xaml` + `.xaml.cs`, `Views/TransportBar.xaml` + `.xaml.cs` (new).
- `dotnet/src/TelemetryPoc.App/Views/OverviewView.xaml` (modify — host gauge grid).
- `dotnet/src/TelemetryPoc.App/RideSession.cs` (modify — `DurationMs`, `RideMs`).
- `dotnet/src/TelemetryPoc.App/MainWindow.xaml` (modify — add footer row).

---

### Task 1: Viz gauge math + formatting + short clock (pure, xUnit)

**Files:**
- Create: `GaugeFormat.cs`, `GaugeViz.cs`
- Modify: `MissionClock.cs`
- Create: `GaugeFormatTests.cs`, `GaugeVizTests.cs`; Modify: `MissionClockTests.cs`

**Interfaces:**
- Produces:
  - `GaugeFormat.Num(double)→string`, `GaugeFormat.Scale(double)→string`, `GaugeFormat.GaugeValue(double)→string`
  - `record GaugeVizResult(double AngleDeg, string Min, string Q1, string Q3, string Max, string ValueText)`
  - `GaugeViz.Compute(double value)→GaugeVizResult`
  - `MissionClock.FormatShort(long ms)→string` (`"H:MM:SS"`, no ms, hours not zero-padded)

- [ ] **Step 1: Write the failing tests** — `GaugeFormatTests.cs`:

```csharp
using TelemetryPoc.App.Viz;
using Xunit;

public class GaugeFormatTests
{
    [Theory]
    [InlineData(0.0, "0.000000")]
    [InlineData(0.5, "0.500000")]
    [InlineData(1.5, "1.500")]
    [InlineData(250.0, "250.0")]
    public void Num_picks_precision_by_magnitude(double v, string s) => Assert.Equal(s, GaugeFormat.Num(v));

    [Fact]
    public void Num_non_finite_is_dash() => Assert.Equal("—", GaugeFormat.Num(double.NaN));

    [Theory]
    [InlineData(0.0, "0")]
    [InlineData(250.0, "250")]
    [InlineData(2.5, "2.5")]
    [InlineData(1.0, "1")]
    [InlineData(0.25, "0.25")]
    [InlineData(0.05, "0.05")]
    public void Scale_strips_trailing_zeros(double v, string s) => Assert.Equal(s, GaugeFormat.Scale(v));

    [Theory]
    [InlineData(250.0, "250.0")]
    [InlineData(1.5, "1.500")]
    public void GaugeValue_two_band(double v, string s) => Assert.Equal(s, GaugeFormat.GaugeValue(v));
}
```

`GaugeVizTests.cs`:

```csharp
using TelemetryPoc.App.Viz;
using Xunit;

public class GaugeVizTests
{
    [Fact]
    public void Zero_centers_needle()
    {
        var g = GaugeViz.Compute(0);
        Assert.Equal(-135 + 0.5 * 270, g.AngleDeg, 6); // 0° at center
    }

    [Fact]
    public void Autoscale_picks_nice_round_R()
    {
        // value 20 → raw=26 → ex=1, ff=2.6 → nf=5 → R=50
        var g = GaugeViz.Compute(20);
        Assert.Equal("50", g.Max);
        Assert.Equal("-50", g.Min);
        Assert.Equal("25", g.Q3);
        Assert.Equal("-25", g.Q1);
    }

    [Fact]
    public void Value_at_R_is_full_right()
    {
        // value 20, R=50 → frac=(20+50)/100=0.7 → angle=-135+0.7*270=54
        var g = GaugeViz.Compute(20);
        Assert.Equal(54, g.AngleDeg, 6);
        Assert.Equal("20.000", g.ValueText);
    }

    [Fact]
    public void Clamps_below_min()
    {
        // value -100, raw=130, ex=2, ff=1.3→nf=2→R=200; frac=(-100+200)/400=0.25→angle=-67.5
        var g = GaugeViz.Compute(-100);
        Assert.Equal(-67.5, g.AngleDeg, 6);
    }
}
```

Append to `MissionClockTests.cs`:

```csharp
public class MissionClockShortTests
{
    [Theory]
    [InlineData(0, "0:00:00")]
    [InlineData(5000, "0:00:05")]
    [InlineData(3_661_000, "1:01:01")]
    public void FormatShort_is_h_mm_ss(long ms, string s) => Assert.Equal(s, MissionClock.FormatShort(ms));
}
```

- [ ] **Step 2: Run → fail** — (from `dotnet/`) `dotnet test --filter "GaugeFormatTests|GaugeVizTests|MissionClockShortTests"`

- [ ] **Step 3: Implement `GaugeFormat.cs`**:

```csharp
using System.Globalization;

namespace TelemetryPoc.App.Viz;

public static class GaugeFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Num(double v)
    {
        if (!double.IsFinite(v)) return "—";
        var a = Math.Abs(v);
        return a >= 100 ? v.ToString("F1", Inv) : a >= 1 ? v.ToString("F3", Inv) : v.ToString("F6", Inv);
    }

    public static string Scale(double v)
    {
        if (v == 0) return "0";
        var a = Math.Abs(v);
        var s = a >= 100 ? v.ToString("F0", Inv)
            : a >= 1 ? v.ToString("F1", Inv)
            : a >= 0.1 ? v.ToString("F2", Inv)
            : v.ToString("F3", Inv);
        if (s.Contains('.')) s = s.TrimEnd('0').TrimEnd('.');
        return s;
    }

    public static string GaugeValue(double v)
    {
        if (!double.IsFinite(v)) return "—";
        return Math.Abs(v) >= 100 ? v.ToString("F1", Inv) : v.ToString("F3", Inv);
    }
}
```

- [ ] **Step 4: Implement `GaugeViz.cs`**:

```csharp
namespace TelemetryPoc.App.Viz;

public sealed record GaugeVizResult(double AngleDeg, string Min, string Q1, string Q3, string Max, string ValueText);

public static class GaugeViz
{
    public static GaugeVizResult Compute(double value)
    {
        var raw = Math.Max(Math.Abs(value) * 1.3, 1e-6);
        var ex = Math.Floor(Math.Log10(raw));
        var ff = raw / Math.Pow(10, ex);
        double nf = ff <= 1 ? 1 : ff <= 2 ? 2 : ff <= 2.5 ? 2.5 : ff <= 5 ? 5 : 10;
        var R = nf * Math.Pow(10, ex);

        var frac = Math.Max(0, Math.Min(1, (value + R) / (2 * R)));
        var angleDeg = -135 + frac * 270;

        return new GaugeVizResult(
            angleDeg,
            GaugeFormat.Scale(-R), GaugeFormat.Scale(-R / 2),
            GaugeFormat.Scale(R / 2), GaugeFormat.Scale(R),
            GaugeFormat.Num(value));
    }
}
```

- [ ] **Step 5: Add `MissionClock.FormatShort`** to `MissionClock.cs` (alongside `Format`):

```csharp
public static string FormatShort(long ms)
{
    if (ms < 0) ms = 0;
    var t = TimeSpan.FromMilliseconds(ms);
    return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}",
        (int)t.TotalHours, t.Minutes, t.Seconds);
}
```

- [ ] **Step 6: Run → pass** — `dotnet test --filter "GaugeFormatTests|GaugeVizTests|MissionClockShortTests"`, then full `dotnet test`.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(dotnet): App.Viz gauge math + number/scale formatting + short clock (xUnit)"
```

---

### Task 2: GaugeView + GaugeViewModel + 2 gauges in OVERVIEW

**Files:**
- Create: `ViewModels/GaugeViewModel.cs`, `Views/GaugeView.xaml` + `.xaml.cs`
- Modify: `ViewModels/OverviewViewModel.cs` (expose `Gauges`), `Views/OverviewView.xaml`

**Interfaces:**
- Consumes: `GaugeViz.Compute`, `GaugeFormat.GaugeValue`, `RideSession`/store.
- Produces: `GaugeViewModel` with `Name`/`Unit`/`AngleDeg`/`ValueText`/`MinLabel`/`Q1Label`/`Q3Label`/`MaxLabel` (INPC) + `Refresh(store)`; `OverviewViewModel.Gauges` (`ObservableCollection<GaugeViewModel>`).

- [ ] **Step 1: Create `ViewModels/GaugeViewModel.cs`**:

```csharp
using System.ComponentModel;
using TelemetryPoc.App.Viz;
using TelemetryPoc.Core;

namespace TelemetryPoc.App.ViewModels;

public sealed class GaugeViewModel : INotifyPropertyChanged
{
    private readonly ChannelMeta _ch;
    public GaugeViewModel(ChannelMeta ch) { _ch = ch; }

    public string Name => _ch.Name;
    public string Unit => _ch.Unit;

    private double _angle = -135;
    public double AngleDeg { get => _angle; private set { _angle = value; Raise(nameof(AngleDeg)); } }

    private string _value = "—";
    public string ValueText { get => _value; private set { _value = value; Raise(nameof(ValueText)); } }

    private string _min = "", _q1 = "", _q3 = "", _max = "";
    public string MinLabel { get => _min; private set { _min = value; Raise(nameof(MinLabel)); } }
    public string Q1Label { get => _q1; private set { _q1 = value; Raise(nameof(Q1Label)); } }
    public string Q3Label { get => _q3; private set { _q3 = value; Raise(nameof(Q3Label)); } }
    public string MaxLabel { get => _max; private set { _max = value; Raise(nameof(MaxLabel)); } }

    public void Refresh(TelemetryStore store)
    {
        var v = store.Latest(_ch.Id) ?? _ch.Min;
        var g = GaugeViz.Compute(v);
        AngleDeg = g.AngleDeg;
        ValueText = GaugeFormat.GaugeValue(v);
        MinLabel = g.Min; Q1Label = g.Q1; Q3Label = g.Q3; MaxLabel = g.Max;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
```

- [ ] **Step 2: Expose `Gauges` in `OverviewViewModel.cs`** — add the collection + build it in `BuildGroups` + refresh it in `RefreshRows`. Add field/property:

```csharp
public ObservableCollection<GaugeViewModel> Gauges { get; } = new();
```

In `BuildGroups()`, after the param-group loop and before `RefreshRows()`, add:

```csharp
Gauges.Clear();
foreach (var ch in store.Channels)
    if (ch.Widget == "gauge") Gauges.Add(new GaugeViewModel(ch));
```

In `RefreshRows()`, after the group loop, add:

```csharp
foreach (var g in Gauges) g.Refresh(_session.Store);
```

- [ ] **Step 3: Create `Views/GaugeView.xaml`** (square dial: arc, needle rotated by AngleDeg, value + labels):

```xml
<UserControl x:Class="TelemetryPoc.App.Views.GaugeView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Width="160" Height="170" Background="{StaticResource Panel}">
    <DockPanel Margin="6">
        <TextBlock DockPanel.Dock="Top" Text="{Binding Name}" Foreground="{StaticResource PanelTitle}"
                   FontFamily="{StaticResource MonoFont}" FontSize="10" HorizontalAlignment="Center" />
        <Grid DockPanel.Dock="Bottom" HorizontalAlignment="Center" Margin="0,2,0,0">
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="{Binding ValueText}" Foreground="{StaticResource TextData}"
                           FontFamily="{StaticResource MonoFont}" FontSize="15" FontWeight="Bold" />
                <TextBlock Text="{Binding Unit}" Foreground="{StaticResource TextDim}"
                           FontFamily="{StaticResource MonoFont}" FontSize="9" VerticalAlignment="Bottom" Margin="3,0,0,2" />
            </StackPanel>
        </Grid>
        <Canvas Width="120" Height="120" HorizontalAlignment="Center" VerticalAlignment="Center">
            <!-- 270° arc, radius 50, center (60,60): start -135° (17.6,95.4) end +135° (102.4,95.4) -->
            <Path Stroke="{StaticResource Border1}" StrokeThickness="6">
                <Path.Data>
                    <PathGeometry>
                        <PathFigure StartPoint="17.6,95.4">
                            <ArcSegment Point="102.4,95.4" Size="50,50" IsLargeArc="True" SweepDirection="Clockwise" />
                        </PathFigure>
                    </PathGeometry>
                </Path.Data>
            </Path>
            <!-- needle: vertical line from center up, rotated by AngleDeg about center -->
            <Line X1="60" Y1="60" X2="60" Y2="16" Stroke="{StaticResource AccentCyan}" StrokeThickness="2.5">
                <Line.RenderTransform>
                    <RotateTransform CenterX="60" CenterY="60" Angle="{Binding AngleDeg}" />
                </Line.RenderTransform>
            </Line>
            <Ellipse Canvas.Left="55" Canvas.Top="55" Width="10" Height="10" Fill="{StaticResource AccentCyan}" />
            <!-- scale labels -->
            <TextBlock Canvas.Left="2"  Canvas.Top="100" Text="{Binding MinLabel}" Foreground="{StaticResource TextDim}" FontFamily="{StaticResource MonoFont}" FontSize="8" />
            <TextBlock Canvas.Left="0"  Canvas.Top="48"  Text="{Binding Q1Label}"  Foreground="{StaticResource TextDim}" FontFamily="{StaticResource MonoFont}" FontSize="8" />
            <TextBlock Canvas.Left="104" Canvas.Top="48"  Text="{Binding Q3Label}"  Foreground="{StaticResource TextDim}" FontFamily="{StaticResource MonoFont}" FontSize="8" />
            <TextBlock Canvas.Left="100" Canvas.Top="100" Text="{Binding MaxLabel}" Foreground="{StaticResource TextDim}" FontFamily="{StaticResource MonoFont}" FontSize="8" />
        </Canvas>
    </DockPanel>
</UserControl>
```

`Views/GaugeView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace TelemetryPoc.App.Views;

public partial class GaugeView : UserControl
{
    public GaugeView() => InitializeComponent();
}
```

- [ ] **Step 4: Host the gauges in `Views/OverviewView.xaml`** — replace the placeholder TextBlock in the right column with a wrap of GaugeViews bound to `Gauges`:

```xml
        <ItemsControl Grid.Column="1" ItemsSource="{Binding Gauges}" Margin="6">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <WrapPanel Orientation="Horizontal" />
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border Margin="6" BorderBrush="{StaticResource Border1}" BorderThickness="1">
                        <views:GaugeView />
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
```

> The `GaugeView`'s DataContext is the `GaugeViewModel` item (set by the ItemTemplate), so its bindings resolve. `xmlns:views` is already declared on OverviewView.

- [ ] **Step 5: Build + launch-verify** — (from `dotnet/`) `dotnet build`; `dotnet run --project src/TelemetryPoc.App`. Confirm 2 gauges (SkyPitch, SkyRoll) in the right column with a 270° arc, a cyan needle that **moves** with the live value, the big value text + unit, and 4 scale labels. Close it. (Controller does the live check.)

- [ ] **Step 6: Full test run** — `dotnet test` → green.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(dotnet): live radial gauge widgets in OVERVIEW"
```

---

### Task 3: Transport footer (read-only)

**Files:**
- Modify: `RideSession.cs` (add `DurationMs`, `RideMs`)
- Create: `ViewModels/TransportViewModel.cs`, `Views/TransportBar.xaml` + `.xaml.cs`
- Modify: `MainWindow.xaml` (add a bottom footer row), `MainWindow.xaml.cs` (set footer DataContext)

**Interfaces:**
- Consumes: `RideSession` (`Store`, `ClockText`, `TPlusText`, `Ticked`, new `DurationMs`/`RideMs`), `MissionClock.FormatShort`.
- Produces: `RideSession.DurationMs` (`long`, from RideMeta), `RideSession.RideMs` (`long`, updated each tick); `TransportViewModel(RideSession)` with `ClockText`/`TPlusText`/`Progress` (0–1 double)/`BufferText`/`SamplesText`/`DroppedText`.

- [ ] **Step 1: Add `DurationMs` + `RideMs` to `RideSession.cs`** — add properties:

```csharp
public long DurationMs { get; private set; }
public long RideMs { get; private set; }
```

In `Start()`, after `var samples = TelemetryDb.LoadSamples(conn, channels);` add:

```csharp
var meta = TelemetryDb.LoadRideMeta(conn);
DurationMs = meta.DurationS * 1000;
```

In `Tick()`, where `var rideMs = (long)(elapsed * _speed);` is computed, set the property right after:

```csharp
RideMs = rideMs;
```

(Keep the rest of Tick unchanged; `Ticked?.Invoke()` already fires at the end.)

- [ ] **Step 2: Create `ViewModels/TransportViewModel.cs`**:

```csharp
using System.ComponentModel;
using System.Globalization;
using TelemetryPoc.App.Viz;

namespace TelemetryPoc.App.ViewModels;

public sealed class TransportViewModel : INotifyPropertyChanged
{
    private readonly RideSession _session;
    public TransportViewModel(RideSession session)
    {
        _session = session;
        _session.Ticked += Refresh;
    }

    public string ClockText => _session.ClockText;
    public string TPlusText => _session.TPlusText;

    private double _progress;
    public double Progress { get => _progress; private set { _progress = value; Raise(nameof(Progress)); } }

    private string _buffer = "0:00:00";
    public string BufferText { get => _buffer; private set { _buffer = value; Raise(nameof(BufferText)); } }

    private string _samples = "0";
    public string SamplesText { get => _samples; private set { _samples = value; Raise(nameof(SamplesText)); } }

    public string DroppedText => "0";

    private void Refresh()
    {
        var dur = _session.DurationMs;
        Progress = dur > 0 ? Math.Max(0, Math.Min(1, (double)_session.RideMs / dur)) : 0;
        BufferText = MissionClock.FormatShort(_session.RideMs);

        var store = _session.Store;
        var maxLen = 0;
        foreach (var ch in store.Channels)
            if (ch.Widget == "strip")
            {
                var len = store.Series(ch.Id)?.Len ?? 0;
                if (len > maxLen) maxLen = len;
            }
        SamplesText = maxLen.ToString(CultureInfo.InvariantCulture);

        Raise(nameof(ClockText));
        Raise(nameof(TPlusText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
```

- [ ] **Step 3: Create `Views/TransportBar.xaml`** (decorative controls + clock + progress + counters):

```xml
<UserControl x:Class="TelemetryPoc.App.Views.TransportBar"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Height="58" Background="{StaticResource Panel2}">
    <Grid Margin="12,6">
        <Grid.RowDefinitions>
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>
        <Grid Grid.Row="0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>
            <TextBlock Grid.Column="0" Text="&#9198;&#160;&#9208;&#160;&#9197;" Foreground="{StaticResource TextDim}"
                       FontSize="14" VerticalAlignment="Center" Margin="0,0,16,0" />
            <StackPanel Grid.Column="1" VerticalAlignment="Center" Margin="0,0,16,0">
                <TextBlock Text="{Binding ClockText}" Foreground="{StaticResource TextData}"
                           FontFamily="{StaticResource MonoFont}" FontSize="15" />
                <TextBlock Text="{Binding TPlusText}" Foreground="{StaticResource TextDim}"
                           FontFamily="{StaticResource MonoFont}" FontSize="9" />
            </StackPanel>
            <ProgressBar Grid.Column="2" VerticalAlignment="Center" Height="4"
                         Minimum="0" Maximum="1" Value="{Binding Progress}"
                         Background="{StaticResource Panel}" Foreground="{StaticResource AccentCyan}"
                         BorderThickness="0" />
        </Grid>
        <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,4,0,0">
            <StackPanel Margin="0,0,18,0">
                <TextBlock Text="BUFFER" Foreground="{StaticResource TextDim}" FontFamily="{StaticResource MonoFont}" FontSize="8" />
                <TextBlock Text="{Binding BufferText}" Foreground="{StaticResource TextData}" FontFamily="{StaticResource MonoFont}" FontSize="10" />
            </StackPanel>
            <StackPanel Margin="0,0,18,0">
                <TextBlock Text="SAMPLES" Foreground="{StaticResource TextDim}" FontFamily="{StaticResource MonoFont}" FontSize="8" />
                <TextBlock Text="{Binding SamplesText}" Foreground="{StaticResource TextData}" FontFamily="{StaticResource MonoFont}" FontSize="10" />
            </StackPanel>
            <StackPanel>
                <TextBlock Text="DROPPED" Foreground="{StaticResource TextDim}" FontFamily="{StaticResource MonoFont}" FontSize="8" />
                <TextBlock Text="{Binding DroppedText}" Foreground="{StaticResource Green}" FontFamily="{StaticResource MonoFont}" FontSize="10" />
            </StackPanel>
        </StackPanel>
    </Grid>
</UserControl>
```

`Views/TransportBar.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace TelemetryPoc.App.Views;

public partial class TransportBar : UserControl
{
    public TransportBar() => InitializeComponent();
}
```

- [ ] **Step 4: Add the footer row in `MainWindow.xaml`** — change the grid to 3 rows (topbar / overview / footer) and add the named TransportBar. Update the RowDefinitions and add the control:

```xml
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>
        <views:TopBar x:Name="TopBar" Grid.Row="0" />
        <views:OverviewView x:Name="Overview" Grid.Row="1" />
        <views:TransportBar x:Name="Transport" Grid.Row="2" />
        <views:Hud Grid.RowSpan="3" />
```

(The `Hud` overlay `Grid.RowSpan` changes from 2 to 3.)

- [ ] **Step 5: Set the footer DataContext in `MainWindow.xaml.cs`** — after the `Overview.DataContext = ...` line add:

```csharp
        Transport.DataContext = new TransportViewModel(_session);
```

- [ ] **Step 6: Build + launch-verify** — (from `dotnet/`) `dotnet build`; `dotnet run --project src/TelemetryPoc.App`. Confirm the bottom footer: decorative transport glyphs, the live clock + `T+`, a cyan progress bar that **advances** with the ride, and BUFFER / SAMPLES (rising) / DROPPED `0`. Close it.

- [ ] **Step 7: Full test run** — `dotnet test` → green.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(dotnet): read-only transport footer (clock/progress/buffer/samples/dropped)"
```

---

## Self-Review

**Spec coverage (Phase 3):** Canvas gauge with geometry + needle + scale labels mirroring gaugeViz.ts (Tasks 1–2); 2 live gauges wired (Task 2); read-only transport footer with clock/T+/progress/BUFFER/SAMPLES/DROPPED (Task 3). ✓

**Placeholder scan:** No TBD/TODO; every step carries complete code. Transport control glyphs are intentionally decorative (perf-focus, no interactions) — stated, not a gap. ✓

**Type consistency:** `GaugeFormat.Num/Scale/GaugeValue`, `GaugeVizResult(AngleDeg,Min,Q1,Q3,Max,ValueText)`, `GaugeViz.Compute`, `MissionClock.FormatShort`, `GaugeViewModel` props, `OverviewViewModel.Gauges`, `RideSession.DurationMs/RideMs`, `TransportViewModel` props — consistent across Tasks 1→3. Gauge math matches the Global Constraints formulae. Test case `Compute(20)`: raw=26, ex=1, ff=2.6→nf=5, R=50, frac=0.7, angle=54 — internally consistent. ✓

**Note:** the gauge value falls back to `ch.Min` when `Latest` is null (matches the old Dashboard.razor gauge behavior), so the needle has a defined rest position before data arrives.
