# .NET Transport Pause / Seek Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the .NET footer transport bar interactive — a play/pause toggle that freezes the ride and click-to-seek on the progress bar that jumps anywhere in the ride — mirroring the Rust app's transport, while the HUD keeps measuring render perf.

**Architecture:** The .NET app drives its own clock, so pause is a *freeze* and seek a *jump* of a ride-clock — no Rust-style `t0` rebase. A new pure `RideClock` (xUnit) holds `RideMs`/`Playing` and only advances while playing; `ReplayPlayer` moves from real-elapsed to ride-ms semantics and gains `SeekTo`; `RideSession` exposes `Pause`/`Resume`/`Seek` and a `Reset` event; the transport VM/view wire a ▶/⏸ button + click-to-seek; on seek the store resets and the chart `DataLogger` + map track clear and refill forward.

**Tech Stack:** .NET 8 (WPF + net8.0 libs), ScottPlot.WPF 5.0.55, SkiaSharp, xUnit.

## Global Constraints

- `TelemetryPoc.Core` and `TelemetryPoc.App.Viz` carry xUnit coverage; WPF/XAML views are build-verified + launch-confirmed (no headless UI test).
- Behaviorally equivalent to Rust: **pause/resume/seek only** — no live speed control, no drag-scrub, no window backfill on seek.
- Seek mirrors Rust's re-meta-on-seek: reset the telemetry store, jump to the first sample `≥ target`, emit one frame; strip charts / map track refill forward.
- The HUD must keep updating while paused (it measures render perf): the `DispatcherTimer` keeps ticking; only the scaled ride-clock freezes.
- Speed comes from `RIDE_SPEED` env only (unchanged). Keep both stacks' forward-play output identical (`ts/speed ≤ elapsed ⟺ ts ≤ rideMs`).
- `RangeBase`/`Selector` reads bind `Mode=OneWay` (the `ProgressBar.Value` TwoWay-write crash precedent).

## File Structure

- `dotnet/src/TelemetryPoc.App.Viz/RideClock.cs` (new) — pure ride-clock: `RideMs`, `Playing`, `Advance`/`Pause`/`Resume`/`SeekTo`.
- `dotnet/tests/TelemetryPoc.Core.Tests/RideClockTests.cs` (new) — `RideClock` unit tests.
- `dotnet/src/TelemetryPoc.Core/ReplayPlayer.cs` (modify) — ride-ms `Advance` + `SeekTo`; drop `Pacer`.
- `dotnet/tests/TelemetryPoc.Core.Tests/ReplayPlayerTests.cs` (modify) — rewrite to ride-ms; add `SeekTo` tests.
- `dotnet/src/TelemetryPoc.App/RideSession.cs` (modify) — `RideClock` integration, `Pause`/`Resume`/`Seek`, `IsPaused`, `Reset` event.
- `dotnet/src/TelemetryPoc.App/ViewModels/TransportViewModel.cs` (modify) — `IsPaused`, `PlayPauseGlyph`, `TogglePlayPause`, `Seek`.
- `dotnet/src/TelemetryPoc.App/Views/TransportBar.xaml` + `.xaml.cs` (modify) — ▶/⏸ button + click-to-seek handler.
- `dotnet/src/TelemetryPoc.App/ViewModels/LineChartViewModel.cs` + `MapWidgetViewModel.cs` (modify) — `Reset` event.
- `dotnet/src/TelemetryPoc.App/ViewModels/OverviewViewModel.cs` (modify) — route `_session.Reset` to child VMs.
- `dotnet/src/TelemetryPoc.App/Views/LineChartView.xaml.cs` + `MapWidgetView.xaml.cs` (modify) — clear on VM `Reset`.

## Existing API (consumed)

- `TelemetryStore.ApplyMeta(channels, enums)` — the reset path (clears strip series, GPS track, latest, `_lastEmit`).
- `TelemetryStore.ApplyFrame(Sample, long emitUnixMs)`; `Sample.TsMs` (long), `Sample.Values` (double[]).
- `RideSession`: `Store`, `DurationMs`, `RideMs`, `GpsBounds`, events `MetaLoaded`/`Ticked`; `Tick()` reads `_sw.ElapsedMilliseconds`.
- `MissionClock.Format(ms)`, `MissionClock.FormatTPlus(ms)`.
- `OverviewViewModel`: `LineCharts` (`ObservableCollection<LineChartViewModel>`), `MapWidget` (`MapWidgetViewModel`); subscribes `_session.Ticked`.
- `LineChartView`: persistent `_logger` (`DataLogger`), `_lastX`; `Detach()` clears both.
- `MapWidgetView`: `Skia.InvalidateVisual()`; track drawn each paint from `_vm.Track` (`store.GpsTrack()`).

---

### Task 1: `RideClock` — pure ride-clock (Viz, xUnit)

**Files:**
- Create: `dotnet/src/TelemetryPoc.App.Viz/RideClock.cs`
- Create: `dotnet/tests/TelemetryPoc.Core.Tests/RideClockTests.cs`

**Interfaces:**
- Produces:
  - `RideClock.RideMs` (long, get), `RideClock.Playing` (bool, get, default true)
  - `void Advance(long scaledDeltaMs)` — adds to `RideMs` only when `Playing && scaledDeltaMs > 0`
  - `void Pause()` / `void Resume()` — flip `Playing`
  - `long SeekTo(long targetMs, long durationMs)` — `RideMs = Clamp(target, 0, max(0,duration))`, returns it; does **not** change `Playing`

- [ ] **Step 1: Write the failing test** — `dotnet/tests/TelemetryPoc.Core.Tests/RideClockTests.cs`:

```csharp
using TelemetryPoc.App.Viz;
using Xunit;

namespace TelemetryPoc.Core.Tests;

public class RideClockTests
{
    [Fact]
    public void Advance_accumulates_when_playing()
    {
        var c = new RideClock();
        c.Advance(100);
        c.Advance(50);
        Assert.Equal(150, c.RideMs);
        Assert.True(c.Playing);
    }

    [Fact]
    public void Advance_is_noop_when_paused()
    {
        var c = new RideClock();
        c.Advance(100);
        c.Pause();
        c.Advance(100);
        Assert.Equal(100, c.RideMs);
        Assert.False(c.Playing);
    }

    [Fact]
    public void Resume_lets_clock_advance_again()
    {
        var c = new RideClock();
        c.Pause();
        c.Advance(100);
        c.Resume();
        c.Advance(40);
        Assert.Equal(40, c.RideMs);
    }

    [Fact]
    public void Advance_ignores_zero_and_negative_delta()
    {
        var c = new RideClock();
        c.Advance(0);
        c.Advance(-25);
        Assert.Equal(0, c.RideMs);
    }

    [Fact]
    public void SeekTo_clamps_low_high_and_midrange()
    {
        var c = new RideClock();
        Assert.Equal(0, c.SeekTo(-500, 1000));      // below 0
        Assert.Equal(1000, c.SeekTo(9999, 1000));   // above duration
        Assert.Equal(400, c.SeekTo(400, 1000));     // mid-range
        Assert.Equal(400, c.RideMs);
    }

    [Fact]
    public void SeekTo_does_not_change_playing()
    {
        var c = new RideClock();
        c.Pause();
        c.SeekTo(300, 1000);
        Assert.False(c.Playing);
        Assert.Equal(300, c.RideMs);
    }
}
```

- [ ] **Step 2: Run → fail** — (from `dotnet/`) `dotnet test --filter RideClockTests`
  Expected: FAIL (compile error — `RideClock` does not exist).

- [ ] **Step 3: Implement `RideClock.cs`**:

```csharp
namespace TelemetryPoc.App.Viz;

/// <summary>The .NET app drives its own replay clock. Pause freezes it; seek jumps it.
/// Speed scaling happens in the caller (delta × speed) before <see cref="Advance"/>.</summary>
public sealed class RideClock
{
    public long RideMs { get; private set; }
    public bool Playing { get; private set; } = true;

    public void Advance(long scaledDeltaMs)
    {
        if (!Playing || scaledDeltaMs <= 0) return;
        RideMs += scaledDeltaMs;
    }

    public void Pause() => Playing = false;
    public void Resume() => Playing = true;

    public long SeekTo(long targetMs, long durationMs)
    {
        var max = durationMs > 0 ? durationMs : 0;
        RideMs = System.Math.Clamp(targetMs, 0, max);
        return RideMs;
    }
}
```

- [ ] **Step 4: Run → pass** — `dotnet test --filter RideClockTests`, then full `dotnet test`.
  Expected: PASS (all green).

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/TelemetryPoc.App.Viz/RideClock.cs dotnet/tests/TelemetryPoc.Core.Tests/RideClockTests.cs
git commit -m "feat(dotnet): RideClock — pause/seek-aware replay clock (xUnit)"
```

---

### Task 2: `ReplayPlayer` — ride-ms semantics + `SeekTo` (Core, xUnit)

**Files:**
- Modify: `dotnet/src/TelemetryPoc.Core/ReplayPlayer.cs`
- Modify: `dotnet/tests/TelemetryPoc.Core.Tests/ReplayPlayerTests.cs`

**Interfaces:**
- Produces:
  - `ReplayPlayer(IReadOnlyList<Sample> samples, TelemetryStore store)` — **no `speed` arg** (speed moved to `RideClock`)
  - `int Advance(long rideMs, long nowUnixMs)` — applies every sample with `TsMs <= rideMs`; returns count applied
  - `int SeekTo(long rideMs)` — sets `_next` to the first index with `TsMs >= rideMs` (or `count`); returns `_next`
  - `long? PeekTs` — `TsMs` of the sample at `_next`, or `null` if `Done`
  - `bool Done` — unchanged
- Consumes: `Sample.TsMs`, `TelemetryStore.ApplyFrame`.

- [ ] **Step 1: Rewrite the test file** — replace `dotnet/tests/TelemetryPoc.Core.Tests/ReplayPlayerTests.cs` entirely:

```csharp
// dotnet/tests/TelemetryPoc.Core.Tests/ReplayPlayerTests.cs
using Xunit;

namespace TelemetryPoc.Core.Tests;

public class ReplayPlayerTests
{
    private static (IReadOnlyList<ChannelMeta>, IReadOnlyList<EnumValue>, List<Sample>) Setup()
    {
        var channels = new List<ChannelMeta>
        {
            new(1, "Roll", "roll", "deg", "real", -180, 180, "strip", 1, "I_01"),
        };
        var samples = new List<Sample>
        {
            new(0, new double[] { 1 }),
            new(100, new double[] { 2 }),
            new(200, new double[] { 3 }),
        };
        return (channels, new List<EnumValue>(), samples);
    }

    private static (ReplayPlayer, TelemetryStore) NewPlayer()
    {
        var (ch, ev, samples) = Setup();
        var store = new TelemetryStore();
        store.ApplyMeta(ch, ev);
        return (new ReplayPlayer(samples, store), store);
    }

    [Fact]
    public void Advance_applies_only_samples_at_or_before_rideMs()
    {
        var (player, store) = NewPlayer();
        Assert.Equal(1, player.Advance(0, 1000));      // only ts=0 due
        Assert.Equal(1.0, store.Latest(1));
        Assert.Equal(2, player.Advance(250, 1000));    // ts=100,200 now due
        Assert.Equal(3.0, store.Latest(1));
        Assert.True(player.Done);
        Assert.Equal(0, player.Advance(999, 1000));    // nothing left
    }

    [Fact]
    public void Advance_applies_all_when_rideMs_past_last()
    {
        var (player, store) = NewPlayer();
        Assert.Equal(3, player.Advance(200, 5));       // ts 0,100,200 all <= 200
        Assert.Equal(5, store.LastEmitUnixMs);
    }

    [Fact]
    public void SeekTo_lands_on_first_sample_at_or_after_target()
    {
        var (player, _) = NewPlayer();
        Assert.Equal(1, player.SeekTo(50));    // first ts >= 50 is index 1 (ts=100)
        Assert.Equal(100, player.PeekTs);
    }

    [Fact]
    public void SeekTo_before_first_is_index_zero()
    {
        var (player, _) = NewPlayer();
        Assert.Equal(0, player.SeekTo(-10));
        Assert.Equal(0, player.PeekTs);
    }

    [Fact]
    public void SeekTo_past_last_is_count_and_done()
    {
        var (player, _) = NewPlayer();
        Assert.Equal(3, player.SeekTo(999));
        Assert.True(player.Done);
        Assert.Null(player.PeekTs);
    }

    [Fact]
    public void Advance_after_SeekTo_applies_from_new_index()
    {
        var (player, store) = NewPlayer();
        player.SeekTo(200);                            // jump to ts=200
        Assert.Equal(1, player.Advance(200, 7));       // applies only ts=200
        Assert.Equal(3.0, store.Latest(1));
        Assert.True(player.Done);
    }
}
```

- [ ] **Step 2: Run → fail** — (from `dotnet/`) `dotnet test --filter ReplayPlayerTests`
  Expected: FAIL (the old `ReplayPlayer` ctor takes `speed`; new tests call `new ReplayPlayer(samples, store)` and use `SeekTo`/`PeekTs`).

- [ ] **Step 3: Rewrite `ReplayPlayer.cs`**:

```csharp
namespace TelemetryPoc.Core;

public sealed class ReplayPlayer
{
    private readonly IReadOnlyList<Sample> _samples;
    private readonly TelemetryStore _store;
    private int _next;

    public ReplayPlayer(IReadOnlyList<Sample> samples, TelemetryStore store)
    {
        _samples = samples;
        _store = store;
    }

    public bool Done => _next >= _samples.Count;

    /// <summary>TsMs of the sample at the playhead, or null if past the end.</summary>
    public long? PeekTs => _next < _samples.Count ? _samples[_next].TsMs : null;

    /// <summary>Apply every sample whose TsMs is at or before the ride clock (ms from ride start).</summary>
    public int Advance(long rideMs, long nowUnixMs)
    {
        int applied = 0;
        while (_next < _samples.Count && _samples[_next].TsMs <= rideMs)
        {
            _store.ApplyFrame(_samples[_next], nowUnixMs);
            _next++;
            applied++;
        }
        return applied;
    }

    /// <summary>Move the playhead to the first sample with TsMs &gt;= rideMs (lower bound). Returns the new index.</summary>
    public int SeekTo(long rideMs)
    {
        int lo = 0, hi = _samples.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (_samples[mid].TsMs < rideMs) lo = mid + 1;
            else hi = mid;
        }
        _next = lo;
        return _next;
    }
}
```

- [ ] **Step 4: Run → pass** — `dotnet test --filter ReplayPlayerTests`.
  Expected: PASS. (Note: `Pacer` is now unused by `ReplayPlayer`; `Pacer.cs` and `PacerTests.cs` stay untouched and still pass.)

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/TelemetryPoc.Core/ReplayPlayer.cs dotnet/tests/TelemetryPoc.Core.Tests/ReplayPlayerTests.cs
git commit -m "refactor(dotnet): ReplayPlayer ride-ms Advance + SeekTo lower-bound (xUnit)"
```

---

### Task 3: `RideSession` — `RideClock` integration + Pause/Resume/Seek + Reset (App, build)

**Files:**
- Modify: `dotnet/src/TelemetryPoc.App/RideSession.cs`

**Interfaces:**
- Consumes: `RideClock` (Task 1), `ReplayPlayer(samples, store)` / `Advance(rideMs, now)` / `SeekTo(rideMs)` / `PeekTs` (Task 2).
- Produces: `void Pause()`, `void Resume()`, `bool IsPaused`, `void Seek(double fraction)`, `event Action? Reset`.

- [ ] **Step 1: Add fields + clock** — in `RideSession.cs`, add a `RideClock`, retain channels/enums/samples for seek reset, and a last-elapsed tracker. Replace the field block (lines 15-21) so it reads:

```csharp
    public TelemetryStore Store { get; } = new();
    private readonly MetricsSampler _metrics = new();
    private readonly Stopwatch _sw = new();
    private readonly RideClock _clock = new();
    private ReplayPlayer? _player;
    private DispatcherTimer? _timer;
    private double _speed = 1.0;
    private long _lastMetricSec = -1;
    private long _lastElapsed;
    private IReadOnlyList<ChannelMeta> _channels = Array.Empty<ChannelMeta>();
    private IReadOnlyList<EnumValue> _enums = Array.Empty<EnumValue>();
    private IReadOnlyList<Sample> _samples = Array.Empty<Sample>();
```

- [ ] **Step 2: Declare the Reset event** — add beside the existing events (after `public event Action? Ticked;`):

```csharp
    public event Action? Reset;
    public bool IsPaused => !_clock.Playing;
```

- [ ] **Step 3: Retain loaded data + new player ctor** — in `Start()`, where channels/enums/samples are loaded and the player is created (lines ~49-71), store them on the fields and use the no-speed ctor. Change:

```csharp
            var channels = TelemetryDb.LoadChannels(conn);
            var enums = TelemetryDb.LoadEnumValues(conn);
            var samples = TelemetryDb.LoadSamples(conn, channels);
```
to keep the locals **and** assign fields right after `DurationMs` is set:

```csharp
            _channels = channels;
            _enums = enums;
            _samples = samples;
```
and change the player construction:

```csharp
            _player = new ReplayPlayer(samples, Store);
```

(Leave the `RIDE_SPEED` parse into `_speed` as-is — it now scales the delta fed to `_clock`.)

- [ ] **Step 4: Rewrite `Tick()`** — drive the clock by delta and the player by ride-ms. Replace the whole `Tick()` method:

```csharp
    private void Tick()
    {
        var elapsed = _sw.ElapsedMilliseconds;
        var delta = elapsed - _lastElapsed;
        _lastElapsed = elapsed;
        _clock.Advance((long)(delta * _speed));

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _player?.Advance(_clock.RideMs, now);

        var rideMs = _clock.RideMs;
        RideMs = rideMs;
        var rideSec = rideMs / 1000;
        if (Store.LastEmitUnixMs > 0 && rideSec != _lastMetricSec)
        {
            _lastMetricSec = rideSec;
            Store.ApplyMetrics(_metrics.Sample());
        }

        ClockText = MissionClock.Format(rideMs);
        TPlusText = MissionClock.FormatTPlus(rideMs);
        Ticked?.Invoke();
    }
```

- [ ] **Step 5: Add Pause/Resume/Seek** — add after `Tick()`:

```csharp
    public void Pause() => _clock.Pause();
    public void Resume() => _clock.Resume();

    public void Seek(double fraction)
    {
        if (_player is null) return;
        var target = (long)(Math.Clamp(fraction, 0, 1) * DurationMs);

        // Reset the store to the re-meta state (clears strip series, GPS track, latest).
        Store.ApplyMeta(_channels, _enums);

        _player.SeekTo(target);
        var snapped = _player.PeekTs ?? target;   // snap to the landed sample so one frame shows
        _clock.SeekTo(snapped, DurationMs);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _player.Advance(_clock.RideMs, now);

        RideMs = _clock.RideMs;
        _lastMetricSec = -1;
        ClockText = MissionClock.Format(RideMs);
        TPlusText = MissionClock.FormatTPlus(RideMs);
        Reset?.Invoke();   // fires BEFORE Ticked so views clear, then repaint with the landed frame
        Ticked?.Invoke();
    }
```

- [ ] **Step 6: Build** — (from `dotnet/`) `dotnet build`.
  Expected: 0 errors. (`Sample` and `EnumValue` are in `TelemetryPoc.Core`, already imported via `using TelemetryPoc.Core;`.) Then `dotnet test` — 0 failures (Core/Viz suites unaffected by App changes).

- [ ] **Step 7: Commit**

```bash
git add dotnet/src/TelemetryPoc.App/RideSession.cs
git commit -m "feat(dotnet): RideSession pause/resume/seek via RideClock + Reset event"
```

---

### Task 4: Transport VM + bar — play/pause button + click-to-seek (App, build + live)

**Files:**
- Modify: `dotnet/src/TelemetryPoc.App/ViewModels/TransportViewModel.cs`
- Modify: `dotnet/src/TelemetryPoc.App/Views/TransportBar.xaml`
- Modify: `dotnet/src/TelemetryPoc.App/Views/TransportBar.xaml.cs`

**Interfaces:**
- Consumes: `RideSession.Pause()`/`Resume()`/`IsPaused`/`Seek(double)` (Task 3).
- Produces: `TransportViewModel.IsPaused`, `PlayPauseGlyph`, `TogglePlayPause()`, `Seek(double fraction)`.

- [ ] **Step 1: Extend `TransportViewModel`** — add the play/pause + seek surface. Insert after the `DroppedText` property (line 28):

```csharp
    public bool IsPaused => _session.IsPaused;
    public string PlayPauseGlyph => _session.IsPaused ? "▶" : "⏸"; // ▶ / ⏸

    public void TogglePlayPause()
    {
        if (_session.IsPaused) _session.Resume();
        else _session.Pause();
        Raise(nameof(IsPaused));
        Raise(nameof(PlayPauseGlyph));
    }

    public void Seek(double fraction) => _session.Seek(fraction);
```

- [ ] **Step 2: Build the play/pause button + seekable bar in XAML** — in `TransportBar.xaml`, replace the static control `TextBlock` (lines 16-17) with a flat button, and make the progress bar's cell click-to-seek. Replace the `<TextBlock ... Text="&#9198;..." />` element with:

```xml
            <Button Grid.Column="0" Click="OnPlayPause" Content="{Binding PlayPauseGlyph}"
                    Foreground="{StaticResource TextDim}" Background="Transparent" BorderThickness="0"
                    FontSize="16" Padding="6,0" Cursor="Hand" VerticalAlignment="Center" Margin="0,0,16,0">
                <Button.Template>
                    <ControlTemplate TargetType="Button">
                        <Border Background="{TemplateBinding Background}">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
                        </Border>
                    </ControlTemplate>
                </Button.Template>
            </Button>
```

And replace the `<ProgressBar Grid.Column="2" .../>` element with a clickable wrapper (a transparent `Border` captures clicks across the full track height):

```xml
            <Border Grid.Column="2" Background="Transparent" Cursor="Hand"
                    MouseLeftButtonDown="OnSeekClick" VerticalAlignment="Center">
                <ProgressBar x:Name="ProgressTrack" Height="4"
                             Minimum="0" Maximum="1" Value="{Binding Progress, Mode=OneWay}"
                             Background="{StaticResource Panel}" Foreground="{StaticResource AccentCyan}"
                             BorderThickness="0" />
            </Border>
```

- [ ] **Step 3: Add code-behind handlers** — replace the contents of `TransportBar.xaml.cs` with:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TelemetryPoc.App.ViewModels;

namespace TelemetryPoc.App.Views;

public partial class TransportBar : UserControl
{
    public TransportBar() => InitializeComponent();

    private void OnPlayPause(object sender, RoutedEventArgs e)
        => (DataContext as TransportViewModel)?.TogglePlayPause();

    private void OnSeekClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement track || track.ActualWidth < 1) return;
        if (DataContext is not TransportViewModel vm) return;
        var x = e.GetPosition(track).X;
        vm.Seek(System.Math.Clamp(x / track.ActualWidth, 0, 1));
    }
}
```

> If `TransportBar.xaml.cs` already has a non-empty body, keep its existing `using`s/namespace and just add the two handler methods + ensure the ctor calls `InitializeComponent()`. The class is `partial`; do not duplicate members.

- [ ] **Step 4: Build** — (from `dotnet/`) `dotnet build`.
  Expected: 0 errors. `dotnet test` — unchanged (no new pure logic here).

- [ ] **Step 5: Launch-verify** — run the app:

```
RIDE_DB=<abs>/data/ride.db RIDE_MBTILES=<abs>/tiles/israel.mbtiles RIDE_SPEED=5 dotnet run --project src/TelemetryPoc.App
```

Confirm: the footer shows a **⏸** button that toggles to **▶**; clicking it **freezes the clock** (and charts/gauges/map stop) and resumes; clicking along the **progress bar jumps** the ride to that position; the **HUD FPS/frame-time keeps updating while paused**. (Controller does the live check.)

- [ ] **Step 6: Commit**

```bash
git add dotnet/src/TelemetryPoc.App/ViewModels/TransportViewModel.cs dotnet/src/TelemetryPoc.App/Views/TransportBar.xaml dotnet/src/TelemetryPoc.App/Views/TransportBar.xaml.cs
git commit -m "feat(dotnet): transport play/pause button + click-to-seek progress bar"
```

---

### Task 5: Seek reset wiring — clear chart + map on seek (App, build + live)

**Files:**
- Modify: `dotnet/src/TelemetryPoc.App/ViewModels/LineChartViewModel.cs`
- Modify: `dotnet/src/TelemetryPoc.App/ViewModels/MapWidgetViewModel.cs`
- Modify: `dotnet/src/TelemetryPoc.App/ViewModels/OverviewViewModel.cs`
- Modify: `dotnet/src/TelemetryPoc.App/Views/LineChartView.xaml.cs`
- Modify: `dotnet/src/TelemetryPoc.App/Views/MapWidgetView.xaml.cs`

**Interfaces:**
- Consumes: `RideSession.Reset` (Task 3).
- Produces: `LineChartViewModel.Reset` (event), `MapWidgetViewModel.Reset` (event), both raised from `OverviewViewModel` on `_session.Reset`.

**Why:** on seek the store is reset (series/track cleared), but the chart's persistent `DataLogger` and the map's draw state don't auto-clear. The `Reset` event clears the logger (`_lastX` too) and invalidates the map so both refill forward from the seek point.

- [ ] **Step 1: Add `Reset` to `LineChartViewModel`** — add beside `Updated` (after line 20):

```csharp
    public event Action? Reset;
    public void RaiseReset() => Reset?.Invoke();
```

- [ ] **Step 2: Add `Reset` to `MapWidgetViewModel`** — add beside `Updated` (after line 15):

```csharp
    public event Action? Reset;
    public void RaiseReset() => Reset?.Invoke();
```

- [ ] **Step 3: Route the session Reset in `OverviewViewModel`** — in the constructor (after `session.Ticked += MapWidget.Tick;`, line 30) subscribe:

```csharp
        _session.Reset += OnReset;
```

and add the handler after `RefreshRows()` (after line 54):

```csharp
    private void OnReset()
    {
        foreach (var lc in LineCharts) lc.RaiseReset();
        MapWidget.RaiseReset();
    }
```

- [ ] **Step 4: Clear the chart logger on VM Reset** — in `LineChartView.xaml.cs`, subscribe to `Reset` alongside `Updated` and clear. In `OnDataContextChanged`, where `_vm.Updated += Redraw;` is set (line 49), add right after:

```csharp
            _vm.Reset += OnReset;
```

In `Detach()`, where `_vm.Updated -= Redraw;` is (line 56), add right after:

```csharp
            if (_vm is not null) _vm.Reset -= OnReset;
```

> Note the existing `Detach()` order: it reads `if (_vm is not null) _vm.Updated -= Redraw;` then `_vm = null;`. Put the `Reset -= OnReset` line **before** `_vm = null;` (i.e. inside/next to the same null-checked line). Add the handler method:

```csharp
    private void OnReset()
    {
        _logger?.Clear();
        _lastX = double.NegativeInfinity;
    }
```

- [ ] **Step 5: Invalidate the map on VM Reset** — in `MapWidgetView.xaml.cs`, subscribe alongside `Updated`. In `OnDataContextChanged`, where `_vm.Updated += OnTick;` is (line 27):

```csharp
        if (_vm is not null) { _vm.Updated += OnTick; _vm.Reset += OnReset; }
```

In `Detach()`, where `_vm.Updated -= OnTick;` is (line 32):

```csharp
        if (_vm is not null) { _vm.Updated -= OnTick; _vm.Reset -= OnReset; }
```

Add the handler (the track is drawn each paint from the now-empty store, so a repaint is all that's needed):

```csharp
    private void OnReset() => Skia.InvalidateVisual();
```

- [ ] **Step 6: Build** — (from `dotnet/`) `dotnet build`.
  Expected: 0 errors. `dotnet test` — unchanged (App view wiring is build-verified).

- [ ] **Step 7: Launch-verify** — run as in Task 4 Step 5. Let the ride run ~20s so the cyan lines and the GPS track build up, then **click near the start of the progress bar** (seek backward): confirm the **strip charts clear and the GPS track resets** to the seek point, then both **refill forward**. Seek forward and confirm the same clean reset. (Controller does the live check.)

- [ ] **Step 8: Commit**

```bash
git add dotnet/src/TelemetryPoc.App/ViewModels/LineChartViewModel.cs dotnet/src/TelemetryPoc.App/ViewModels/MapWidgetViewModel.cs dotnet/src/TelemetryPoc.App/ViewModels/OverviewViewModel.cs dotnet/src/TelemetryPoc.App/Views/LineChartView.xaml.cs dotnet/src/TelemetryPoc.App/Views/MapWidgetView.xaml.cs
git commit -m "feat(dotnet): clear chart logger + map track on seek (Reset wiring)"
```

---

## Self-Review

**Spec coverage:**
- Clock model (delta-accumulating, freeze on pause) → Task 1 (`RideClock`) + Task 3 (`Tick` delta drive). ✓
- `ReplayPlayer` ride-ms `Advance` + `SeekTo` lower-bound, `Pacer` dropped from player → Task 2. ✓
- `RideSession` Pause/Resume/Seek, store reset, snap-to-sample one-frame, `Reset` before `Ticked` → Task 3. ✓
- `TransportViewModel` IsPaused/PlayPauseGlyph/TogglePlayPause/Seek → Task 4. ✓
- `TransportBar` ▶/⏸ button + click-to-seek (click only) → Task 4. ✓
- HUD live while paused (timer keeps ticking, only clock freezes) → Task 3 Step 4 (timer untouched) + Task 4 live check. ✓
- View reset (chart DataLogger + map track refill forward) → Task 5. ✓
- Non-goals (no speed UI, no drag, no backfill) → respected (click-only seek, no speed control added, store-reset-then-forward refill). ✓

**Placeholder scan:** No TBD/TODO. Every code step shows full code. The one conditional note (Task 4 Step 3 "if the file already has a body") gives explicit handling. ✓

**Type consistency:** `RideClock.Advance(long)`/`SeekTo(long,long)→long`/`Pause`/`Resume`/`Playing`/`RideMs` consistent across Tasks 1 & 3. `ReplayPlayer(samples, store)` / `Advance(long,long)→int` / `SeekTo(long)→int` / `PeekTs:long?` consistent across Tasks 2 & 3. `RideSession.Pause/Resume/IsPaused/Seek(double)/Reset` consistent across Tasks 3, 4, 5. `LineChartViewModel.Reset`/`RaiseReset()` and `MapWidgetViewModel.Reset`/`RaiseReset()` consistent across Tasks 5 (defined) and `OverviewViewModel.OnReset` (consumer). `PlayPauseGlyph`/`TogglePlayPause`/`Seek` consistent Task 4 VM ↔ code-behind. ✓

**Note:** `Pacer.cs` + `PacerTests.cs` remain (no longer used by `ReplayPlayer`, but harmless and still green) — YAGNI says don't delete a passing, public, tested helper as a side effect of this feature; if a later cleanup wants it gone, that's a separate change.
