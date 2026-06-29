# .NET WPF INU Reskin — Phase 6 (Perf HUD + Alarm/Caution + Polish) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the **perf HUD** (FPS / frame time / latency / CPU% / RAM — the comparison deliverable), wire the TopBar **ALARM/CAUTION** pills, and apply two cosmetic polish fixes — completing the .NET reskin.

**Architecture:** Pure metrics logic (`FpsMeter`, `LatencyMs`, `StatusCounts`) goes in `TelemetryPoc.App.Viz` (xUnit), mirroring the Rust `hud/fps.ts`. A `HudViewModel` drives FPS off `CompositionTarget.Rendering` and reads latency + CPU/RAM from the store each tick; `Hud.xaml` becomes the real overlay. A `TopBarViewModel` exposes the clock + alarm/caution counts. WPF/XAML build-verified + launch-confirmed.

**Tech Stack:** .NET 8 WPF, xUnit, existing `TelemetryPoc.Core` (`MetricsSampler`, `TelemetryStore`).

## Global Constraints

- `TelemetryPoc.Core` unchanged. Pure logic in `TelemetryPoc.App.Viz` + xUnit; WPF views build/launch-verified.
- HUD numbers mirror the Rust HUD for a fair comparison: **FPS** + **frame ms** from real render frames (`CompositionTarget.Rendering`, window of timestamps like `hud/fps.ts`); **latency** = `now − store.LastEmitUnixMs`; **CPU%** per-core + **RAM MB** from `store.Metrics` (`MetricsSampler`, already sampled once per ride-second by `RideSession`).
- `FpsMeter` mirrors `rust/src/hud/fps.ts` exactly: window default 60; `Fps = (n−1)*1000/span` (0 if `<2` ticks or `span≤0`); `FrameTimeMs = span/(n−1)`.
- Alarm/caution: **alarm** = count of enum channels currently decoding to severity `"critical"`; **caution** = 0 (this data has no threshold bands — matches the Rust app where only enum severity is critical).
- INU palette: panel `#10151d`, border `#1d2632`, text `#c3ccd8`, dim `#566273`, accent `#38c5e0`, green `#2fd17a`, amber `#f5b440`, red `#ff4d52`.
- IBM Plex `.ttf` bundling is **out of scope** (needs a network download; the Segoe/Consolas fallback stays) — noted as an optional follow-up.

## Existing API (consumed)

- `TelemetryStore`: `Channels` (`IReadOnlyList<ChannelMeta>`), `Latest(long)→double?`, `EnumIndex`, `LastEmitUnixMs`, `Metrics` (`Metrics(double CpuPct, double RamMb)?`).
- `ValueFormat.DecodeEnum(long, double, index)→EnumValue?` (severity).
- `RideSession`: `Store`, `ClockText`/`TPlusText`, `MetaLoaded`/`Ticked`, `Start()`.
- `TopBar.xaml` (Phase 1): a 3-column grid; right `StackPanel` holds `LINK …` + a named `TextBlock x:Name="ClockText"`. MainWindow currently binds `TopBar.ClockText.Text` to `RideSession.ClockText` imperatively.
- `Hud.xaml` (Phase 1): a placeholder chip overlay (top-right).
- `GaugeView.xaml`: the 270° arc Path (Phase 3 minor: endpoints slightly off the r=50 circle).
- `BasemapRenderer.cs` `DrawLabels`: a shared `halo` paint `TextSize=11` used for road labels whose fill is `TextSize=10`.

## File Structure

- `dotnet/src/TelemetryPoc.App.Viz/FpsMeter.cs` (new) — `FpsMeter`, `Hud.LatencyMs`.
- `dotnet/src/TelemetryPoc.App.Viz/StatusCounts.cs` (new) — alarm/caution counts.
- `dotnet/src/TelemetryPoc.App/ViewModels/HudViewModel.cs` (new).
- `dotnet/src/TelemetryPoc.App/ViewModels/TopBarViewModel.cs` (new).
- `dotnet/src/TelemetryPoc.App/Views/Hud.xaml` (rewrite — real overlay).
- `dotnet/src/TelemetryPoc.App/Views/TopBar.xaml` (modify — pills + DataContext bindings).
- `dotnet/src/TelemetryPoc.App/MainWindow.xaml.cs` (modify — set Hud + TopBar DataContexts).
- `dotnet/src/TelemetryPoc.App/Views/GaugeView.xaml` (modify — arc endpoints).
- `dotnet/src/TelemetryPoc.Map/BasemapRenderer.cs` (modify — road halo size).
- `dotnet/tests/TelemetryPoc.Core.Tests/FpsMeterTests.cs`, `StatusCountsTests.cs` (new).

---

### Task 1: Pure metrics — FpsMeter + LatencyMs + StatusCounts (xUnit)

**Files:**
- Create: `FpsMeter.cs`, `StatusCounts.cs`, `FpsMeterTests.cs`, `StatusCountsTests.cs`

**Interfaces:**
- Produces:
  - `class FpsMeter` — `FpsMeter(int windowSize = 60)`; `Tick(double tsMs)`; `Fps()→double`; `FrameTimeMs()→double`.
  - `static class HudMetrics` — `LatencyMs(long emitUnixMs, long nowUnixMs)→long`.
  - `static class StatusCounts` — `Compute(IReadOnlyList<ChannelMeta> channels, Func<long,double?> latest, IReadOnlyDictionary<(long,long),EnumValue> enumIndex)→(int Alarms, int Cautions)`.

- [ ] **Step 1: Write the failing tests** — `FpsMeterTests.cs`:

```csharp
using TelemetryPoc.App.Viz;
using Xunit;

public class FpsMeterTests
{
    [Fact]
    public void Zero_before_two_ticks()
    {
        var m = new FpsMeter();
        Assert.Equal(0, m.Fps(), 6);
        m.Tick(0);
        Assert.Equal(0, m.Fps(), 6);
    }

    [Fact]
    public void Computes_60fps_from_16_67ms_intervals()
    {
        var m = new FpsMeter();
        for (int i = 0; i <= 10; i++) m.Tick(i * (1000.0 / 60));
        Assert.Equal(60, m.Fps(), 0);
        Assert.Equal(1000.0 / 60, m.FrameTimeMs(), 1);
    }

    [Fact]
    public void Respects_window_size()
    {
        var m = new FpsMeter(3);
        for (int i = 0; i < 10; i++) m.Tick(i * 10);
        Assert.Equal(100, m.Fps(), 0); // last 3 ts → 2×10ms → 100fps
    }

    [Fact]
    public void LatencyMs_is_now_minus_emit()
        => Assert.Equal(75, HudMetrics.LatencyMs(1000, 1075));
}
```

`StatusCountsTests.cs`:

```csharp
using System.Collections.Generic;
using TelemetryPoc.App.Viz;
using TelemetryPoc.Core;
using Xunit;

public class StatusCountsTests
{
    private static ChannelMeta Enum(long id, string col) => new(id, col, col, "", "enum", 0, 1, "table", id, "I_01");
    private static ChannelMeta Real(long id, string col) => new(id, col, col, "deg", "real", -1, 1, "table", id, "I_01");

    [Fact]
    public void Alarms_count_enum_critical_channels()
    {
        var idx = ValueFormat.BuildEnumIndex(new[]
        {
            new EnumValue(1, 0, "Normal", "ok"),
            new EnumValue(1, 1, "Critical", "critical"),
        });
        var channels = new[] { Enum(1, "mode"), Real(2, "roll") };
        double? Latest(long id) => id == 1 ? 1.0 : 0.5; // mode=Critical, roll=ok
        var (alarms, cautions) = StatusCounts.Compute(channels, Latest, idx);
        Assert.Equal(1, alarms);
        Assert.Equal(0, cautions);
    }

    [Fact]
    public void No_alarms_when_enum_is_ok()
    {
        var idx = ValueFormat.BuildEnumIndex(new[] { new EnumValue(1, 0, "Normal", "ok") });
        var channels = new[] { Enum(1, "mode") };
        var (alarms, _) = StatusCounts.Compute(channels, _ => 0.0, idx);
        Assert.Equal(0, alarms);
    }
}
```

- [ ] **Step 2: Run → fail** — (from `dotnet/`) `dotnet test --filter "FpsMeterTests|StatusCountsTests"`

- [ ] **Step 3: Implement `FpsMeter.cs`**:

```csharp
namespace TelemetryPoc.App.Viz;

public sealed class FpsMeter
{
    private readonly List<double> _ts = new();
    private readonly int _window;
    public FpsMeter(int windowSize = 60) { _window = windowSize; }

    public void Tick(double tsMs)
    {
        _ts.Add(tsMs);
        if (_ts.Count > _window) _ts.RemoveAt(0);
    }

    public double Fps()
    {
        if (_ts.Count < 2) return 0;
        var span = _ts[^1] - _ts[0];
        return span <= 0 ? 0 : (_ts.Count - 1) * 1000.0 / span;
    }

    public double FrameTimeMs()
    {
        if (_ts.Count < 2) return 0;
        var span = _ts[^1] - _ts[0];
        return span / (_ts.Count - 1);
    }
}

public static class HudMetrics
{
    public static long LatencyMs(long emitUnixMs, long nowUnixMs) => nowUnixMs - emitUnixMs;
}
```

- [ ] **Step 4: Implement `StatusCounts.cs`**:

```csharp
using TelemetryPoc.Core;

namespace TelemetryPoc.App.Viz;

public static class StatusCounts
{
    public static (int Alarms, int Cautions) Compute(
        IReadOnlyList<ChannelMeta> channels,
        Func<long, double?> latest,
        IReadOnlyDictionary<(long, long), EnumValue> enumIndex)
    {
        int alarms = 0;
        foreach (var ch in channels)
        {
            if (ch.Type != "enum") continue;
            var v = latest(ch.Id);
            if (v is null) continue;
            if (ValueFormat.DecodeEnum(ch.Id, v.Value, enumIndex)?.Severity == "critical") alarms++;
        }
        return (alarms, 0);
    }
}
```

- [ ] **Step 5: Run → pass** — the filtered tests, then full `dotnet test`.

- [ ] **Step 6: Commit** — `feat(dotnet): perf HUD metrics — FpsMeter + LatencyMs + StatusCounts (xUnit)`

---

### Task 2: HudViewModel + HUD overlay

**Files:**
- Create: `ViewModels/HudViewModel.cs`
- Rewrite: `Views/Hud.xaml`
- Modify: `MainWindow.xaml.cs` (set `Hud.DataContext`)

**Interfaces:**
- Consumes: `FpsMeter`/`HudMetrics`, `RideSession`/store.
- Produces: `HudViewModel(RideSession)` with `FpsText`/`FrameText`/`LatencyText`/`CpuText`/`RamText` (INPC).

- [ ] **Step 1: Create `ViewModels/HudViewModel.cs`** (FPS off CompositionTarget.Rendering; latency + CPU/RAM on tick):

```csharp
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Media;
using TelemetryPoc.App.Viz;

namespace TelemetryPoc.App.ViewModels;

public sealed class HudViewModel : INotifyPropertyChanged
{
    private readonly RideSession _session;
    private readonly FpsMeter _fps = new();
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public HudViewModel(RideSession session)
    {
        _session = session;
        CompositionTarget.Rendering += OnRender;
        _session.Ticked += OnTick;
    }

    private string _fpsText = "—", _frameText = "—", _latencyText = "—", _cpuText = "—", _ramText = "—";
    public string FpsText { get => _fpsText; private set { _fpsText = value; Raise(nameof(FpsText)); } }
    public string FrameText { get => _frameText; private set { _frameText = value; Raise(nameof(FrameText)); } }
    public string LatencyText { get => _latencyText; private set { _latencyText = value; Raise(nameof(LatencyText)); } }
    public string CpuText { get => _cpuText; private set { _cpuText = value; Raise(nameof(CpuText)); } }
    public string RamText { get => _ramText; private set { _ramText = value; Raise(nameof(RamText)); } }

    private void OnRender(object? sender, EventArgs e)
    {
        _fps.Tick(_sw.Elapsed.TotalMilliseconds);
        FpsText = _fps.Fps().ToString("F0", Inv);
        FrameText = _fps.FrameTimeMs().ToString("F1", Inv);
    }

    private void OnTick()
    {
        if (_session.Store.LastEmitUnixMs > 0)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            LatencyText = HudMetrics.LatencyMs(_session.Store.LastEmitUnixMs, now).ToString(Inv);
        }
        var m = _session.Store.Metrics;
        if (m is not null)
        {
            CpuText = m.CpuPct.ToString("F0", Inv);
            RamText = m.RamMb.ToString("F0", Inv);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
```

- [ ] **Step 2: Rewrite `Views/Hud.xaml`** (the overlay):

```xml
<UserControl x:Class="TelemetryPoc.App.Views.Hud"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             HorizontalAlignment="Right" VerticalAlignment="Top" Margin="0,52,12,0">
    <Border Background="{StaticResource Panel}" BorderBrush="{StaticResource Border1}"
            BorderThickness="1" CornerRadius="4" Padding="10,6">
        <StackPanel>
            <TextBlock Text="PERF" Foreground="{StaticResource AccentCyan}" FontFamily="{StaticResource MonoFont}"
                       FontSize="9" FontWeight="Bold" Margin="0,0,0,3" />
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="46" />
                </Grid.ColumnDefinitions>
                <Grid.RowDefinitions>
                    <RowDefinition /><RowDefinition /><RowDefinition /><RowDefinition /><RowDefinition />
                </Grid.RowDefinitions>
                <TextBlock Grid.Row="0" Grid.Column="0" Text="FPS" Foreground="{StaticResource TextDim}" FontFamily="{StaticResource MonoFont}" FontSize="9" />
                <TextBlock Grid.Row="0" Grid.Column="1" Text="{Binding FpsText}" Foreground="{StaticResource TextData}" FontFamily="{StaticResource MonoFont}" FontSize="11" HorizontalAlignment="Right" />
                <TextBlock Grid.Row="1" Grid.Column="0" Text="FRAME" Foreground="{StaticResource TextDim}" FontFamily="{StaticResource MonoFont}" FontSize="9" />
                <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding FrameText}" Foreground="{StaticResource TextData}" FontFamily="{StaticResource MonoFont}" FontSize="11" HorizontalAlignment="Right" />
                <TextBlock Grid.Row="2" Grid.Column="0" Text="LAT ms" Foreground="{StaticResource TextDim}" FontFamily="{StaticResource MonoFont}" FontSize="9" />
                <TextBlock Grid.Row="2" Grid.Column="1" Text="{Binding LatencyText}" Foreground="{StaticResource TextData}" FontFamily="{StaticResource MonoFont}" FontSize="11" HorizontalAlignment="Right" />
                <TextBlock Grid.Row="3" Grid.Column="0" Text="CPU %" Foreground="{StaticResource TextDim}" FontFamily="{StaticResource MonoFont}" FontSize="9" />
                <TextBlock Grid.Row="3" Grid.Column="1" Text="{Binding CpuText}" Foreground="{StaticResource Green}" FontFamily="{StaticResource MonoFont}" FontSize="11" HorizontalAlignment="Right" />
                <TextBlock Grid.Row="4" Grid.Column="0" Text="RAM MB" Foreground="{StaticResource TextDim}" FontFamily="{StaticResource MonoFont}" FontSize="9" />
                <TextBlock Grid.Row="4" Grid.Column="1" Text="{Binding RamText}" Foreground="{StaticResource TextData}" FontFamily="{StaticResource MonoFont}" FontSize="11" HorizontalAlignment="Right" />
            </Grid>
        </StackPanel>
    </Border>
</UserControl>
```

- [ ] **Step 3: Set `Hud.DataContext` in `MainWindow.xaml.cs`** — give the Hud instance a name in `MainWindow.xaml` (`<views:Hud x:Name="Hud" Grid.RowSpan="3" />`), then after the existing DataContext assignments add:

```csharp
        Hud.DataContext = new HudViewModel(_session);
```

- [ ] **Step 4: Build + launch-verify** — (from `dotnet/`) `dotnet build`; run the app. Confirm the top-right HUD shows live **FPS / FRAME / LAT / CPU / RAM** updating. Close it.

- [ ] **Step 5: Full test run** — `dotnet test` → green.

- [ ] **Step 6: Commit** — `feat(dotnet): perf HUD overlay (FPS/frame/latency/CPU/RAM)`

---

### Task 3: TopBar alarm/caution + cosmetic polish

**Files:**
- Create: `ViewModels/TopBarViewModel.cs`
- Modify: `Views/TopBar.xaml`, `MainWindow.xaml.cs`, `Views/GaugeView.xaml`, `src/TelemetryPoc.Map/BasemapRenderer.cs`

**Interfaces:**
- Produces: `TopBarViewModel(RideSession)` with `ClockText` + `AlarmText`/`CautionText` (INPC), refreshed on Ticked via `StatusCounts.Compute`.

- [ ] **Step 1: Create `ViewModels/TopBarViewModel.cs`**:

```csharp
using System.ComponentModel;
using System.Globalization;
using TelemetryPoc.App.Viz;

namespace TelemetryPoc.App.ViewModels;

public sealed class TopBarViewModel : INotifyPropertyChanged
{
    private readonly RideSession _session;
    public TopBarViewModel(RideSession session)
    {
        _session = session;
        _session.Ticked += Refresh;
    }

    public string ClockText => _session.ClockText;

    private string _alarm = "0 ALARM", _caution = "0 CAUTION";
    public string AlarmText { get => _alarm; private set { _alarm = value; Raise(nameof(AlarmText)); } }
    public string CautionText { get => _caution; private set { _caution = value; Raise(nameof(CautionText)); } }

    private void Refresh()
    {
        var store = _session.Store;
        var (alarms, cautions) = StatusCounts.Compute(store.Channels, store.Latest, store.EnumIndex);
        AlarmText = string.Format(CultureInfo.InvariantCulture, "{0} ALARM", alarms);
        CautionText = string.Format(CultureInfo.InvariantCulture, "{0} CAUTION", cautions);
        Raise(nameof(ClockText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
```

- [ ] **Step 2: Add the pills + clock binding in `Views/TopBar.xaml`** — in the right `StackPanel`, before the `LINK …` block, add two pills; and change the named `ClockText` TextBlock's `Text` to a binding. The pills:

```xml
            <Border Background="{StaticResource Panel}" CornerRadius="3" Padding="6,2" Margin="0,0,8,0">
                <StackPanel Orientation="Horizontal">
                    <Ellipse Width="6" Height="6" Fill="{StaticResource Red}" VerticalAlignment="Center" Margin="0,0,5,0" />
                    <TextBlock Text="{Binding AlarmText}" Foreground="{StaticResource Red}" FontFamily="{StaticResource MonoFont}" FontSize="10" />
                </StackPanel>
            </Border>
            <Border Background="{StaticResource Panel}" CornerRadius="3" Padding="6,2" Margin="0,0,12,0">
                <StackPanel Orientation="Horizontal">
                    <Ellipse Width="6" Height="6" Fill="{StaticResource Amber}" VerticalAlignment="Center" Margin="0,0,5,0" />
                    <TextBlock Text="{Binding CautionText}" Foreground="{StaticResource Amber}" FontFamily="{StaticResource MonoFont}" FontSize="10" />
                </StackPanel>
            </Border>
```

Change the clock TextBlock to `Text="{Binding ClockText}"` (keep `x:Name="ClockText"` so existing references compile; the binding now drives it):

```xml
            <TextBlock x:Name="ClockText" Text="{Binding ClockText}" Foreground="{StaticResource TextData}"
                       FontFamily="{StaticResource MonoFont}" FontSize="13" />
```

- [ ] **Step 3: Wire `TopBar.DataContext` in `MainWindow.xaml.cs`** — replace the imperative `TopBar.ClockText.SetBinding(...)` line with:

```csharp
        TopBar.DataContext = new TopBarViewModel(_session);
```

(Remove the old `using System.Windows.Data;` clock-binding line if now unused; keep other DataContext assignments.)

- [ ] **Step 4: Polish — gauge arc endpoints** in `Views/GaugeView.xaml`: the 270° arc on a r=50 circle centered at (60,60) starts at −135° and ends at +135°. The true endpoints are `(60 − 50·sin135°, 60 + 50·cos135°) = (24.6, 95.4)` and `(95.4, 95.4)`. Update the `PathFigure`:

```xml
        <PathFigure StartPoint="24.6,95.4">
            <ArcSegment Point="95.4,95.4" Size="50,50" IsLargeArc="True" SweepDirection="Clockwise" />
        </PathFigure>
```

- [ ] **Step 5: Polish — road halo size** in `src/TelemetryPoc.Map/BasemapRenderer.cs` `DrawLabels`: the shared `halo` paint is `TextSize = 11`, but road labels (`roadFill`) are `TextSize = 10`. Set the halo's `TextSize` to match the label's paint before drawing each label. In the final draw loop, where it currently draws the halo with the fixed `halo` paint, set `halo.TextSize = paint.TextSize;` immediately before the halo `DrawText`:

```csharp
        foreach (var (box, paint) in candidates)
        {
            if (!placedSet.Contains(box)) continue;
            halo.TextSize = paint.TextSize;
            canvas.DrawText(box.Text, (float)box.X, (float)(box.Y + box.H), halo);
            canvas.DrawText(box.Text, (float)box.X, (float)(box.Y + box.H), paint);
        }
```

- [ ] **Step 6: Build + launch-verify** — (from `dotnet/`) `dotnet build`; run the app. Confirm: TopBar shows `0 ALARM` (red) + `0 CAUTION` (amber) pills (alarm count rises when `INUMode2` reads Critical) and the clock still ticks; the gauge arc meets the needle sweep cleanly; road labels have a snug halo. Close it.

- [ ] **Step 7: Full test run** — `dotnet test` → green.

- [ ] **Step 8: Commit** — `feat(dotnet): TopBar alarm/caution pills + gauge arc + road-halo polish`

---

## Self-Review

**Spec coverage (reskin spec Phase 6):** perf HUD overlay FPS/frame/latency/CPU/RAM (Tasks 1–2, mirroring `hud/fps.ts`); alarm/caution wiring (Task 3); cosmetic polish — gauge arc + road halo (Task 3). IBM Plex `.ttf` bundling explicitly deferred (network asset) — the constraint states the fallback stays. ✓

**Placeholder scan:** No TBD/TODO. Every step carries complete code. ✓

**Type consistency:** `FpsMeter.Tick/Fps/FrameTimeMs` + `HudMetrics.LatencyMs` consumed by `HudViewModel`; `StatusCounts.Compute(channels, Func<long,double?>, enumIndex)` consumed by `TopBarViewModel` (using `store.Latest` as the `Func`); `HudViewModel`/`TopBarViewModel` props bound in `Hud.xaml`/`TopBar.xaml`; `MainWindow` sets both DataContexts (+ the existing `Overview`/`Transport`). Test values mirror the Rust `fps.test.ts` (60fps from 16.67ms, window-3→100fps, latency 75). ✓

**Note:** `HudViewModel` subscribes `CompositionTarget.Rendering` + `RideSession.Ticked` for the app lifetime (it's a singleton created in `MainWindow` and never torn down) — no unsubscribe needed, matching the other app-lifetime VMs.
