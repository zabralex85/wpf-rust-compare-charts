# .NET Transport Pause / Seek — Design Spec

**Date:** 2026-06-29
**Status:** Approved (user-directed)

## Goal

Make the .NET app's footer transport bar **interactive**, mirroring the Rust
app's transport controls: a **play/pause** toggle that freezes the ride, and
**click-to-seek** on the progress bar that jumps to any point in the ride. The
HUD keeps measuring render perf while paused. Native WPF, behaviorally
equivalent to Rust (pause/resume/seek only — no live speed control, no
drag-scrub, no window backfill).

## Background — why this differs from Rust

Rust replays over a WebSocket using **absolute sample timestamps** and a
`t0` base it **rebases** on pause/seek (`replay.rs`: `t0_new = now - ts/speed`
on seek; `t0_new = t0 + paused_duration` on resume). The .NET app drives its
**own clock** in-process (`RideSession.Tick` reads `_sw.ElapsedMilliseconds`
and derives `RideMs = elapsed * speed`). Because .NET owns the clock, pause is
a **freeze** and seek is a **jump** of a ride-clock — no `t0` rebase math is
needed. Frames already emit when `sample.TsMs ≤ rideMs`.

## Clock model (the core change)

Replace the raw `RideMs = elapsed * speed` (derived straight from the
`Stopwatch`) with a **delta-accumulating ride-clock** that only advances while
playing:

- A new pure `RideClock` holds `RideMs` (long) and `Playing` (bool).
- Each tick, `RideSession` computes `delta = nowElapsed − lastElapsed` (real
  ms since last tick), scales it by `speed`, and calls `clock.Advance(scaledDelta)`.
  `Advance` adds to `RideMs` **only when `Playing`**; while paused the clock
  holds, so no frames become due (the freeze).
- `Pause()` / `Resume()` flip `Playing`. `SeekTo(targetMs, durationMs)` clamps
  the target to `[0, durationMs]` and sets `RideMs`.

The `Stopwatch` keeps running (it's the real-time delta source and the HUD's
frame-time source); only the **scaled ride-clock** freezes on pause.

## Components

### `RideClock` (new — `TelemetryPoc.App.Viz`, pure, xUnit)

```csharp
public sealed class RideClock
{
    public long RideMs { get; private set; }
    public bool Playing { get; private set; } = true;

    public void Advance(long scaledDeltaMs);          // adds when Playing && delta>0
    public void Pause();                              // Playing = false
    public void Resume();                             // Playing = true
    public long SeekTo(long targetMs, long durationMs); // clamp [0,dur], set RideMs, return it
}
```

- `Advance`: if `!Playing` or `scaledDeltaMs <= 0`, no-op; else `RideMs += scaledDeltaMs`.
- `SeekTo`: `RideMs = Math.Clamp(targetMs, 0, Math.Max(0, durationMs))`; returns `RideMs`.
- Seek does **not** change `Playing` — you can seek while paused or while playing.

### `ReplayPlayer` (modify — `TelemetryPoc.Core`, xUnit)

Refactor from real-elapsed semantics to **ride-clock** semantics, and add seek:

```csharp
public int Advance(long rideMs, long nowUnixMs);  // applies samples with TsMs <= rideMs
public int SeekTo(long rideMs);                   // lower-bound: _next = first index with TsMs >= rideMs; returns _next
```

- `Advance(rideMs, …)`: `while (_next < count && _samples[_next].TsMs <= rideMs) ApplyFrame…`.
  This is behaviorally identical to today's `DueOffsetMs(ts) <= elapsed`
  (`ts/speed <= elapsed ⟺ ts <= elapsed*speed = rideMs`), so the forward-play
  output is unchanged — the speed scaling simply moves into `RideClock`.
- `SeekTo(rideMs)`: binary lower-bound over `_samples` by `TsMs`; sets `_next`
  to the first sample `≥ rideMs` (or `count` if none); returns `_next`.
- The `Pacer` dependency is removed from `ReplayPlayer` (the constructor no
  longer takes `speed`); `Pacer.cs` stays for any other consumer. Existing
  `ReplayPlayerTests` are updated to the new `Advance(rideMs, …)` signature
  (same expected frames, expressed in ride-ms instead of elapsed×speed).

### `RideSession` (modify — `TelemetryPoc.App`)

- Hold `_clock = new RideClock()`, the loaded `channels`/`enums` (for store
  reset on seek), and `_lastElapsed` (last tick's `Stopwatch` ms).
- `Tick()`:
  - `elapsed = _sw.ElapsedMilliseconds`; `delta = elapsed − _lastElapsed`;
    `_lastElapsed = elapsed`.
  - `_clock.Advance((long)(delta * _speed))`.
  - `_player.Advance(_clock.RideMs, nowUnix)`; `RideMs = _clock.RideMs`.
  - Metrics cadence + `ClockText`/`TPlusText` + `Ticked` unchanged (driven by
    `RideMs`, which now freezes when paused — so the clock display freezes too).
- `Pause()` → `_clock.Pause()`. `Resume()` → `_clock.Resume()`.
  `IsPaused => !_clock.Playing`.
- `Seek(double fraction)`:
  1. `target = (long)(Math.Clamp(fraction, 0, 1) * DurationMs)`.
  2. Reset the store: `Store.ApplyMeta(_channels, _enums)` (clears strip series,
     GPS track, latest, `_lastEmit` — the existing re-meta reset path).
  3. `int idx = _player.SeekTo(target)`; **snap** the clock to the landed
     sample so one frame shows immediately (mirrors Rust emitting one frame on
     seek): if `idx < samples.Count`, `snapped = samples[idx].TsMs` else `snapped = target`;
     `_clock.SeekTo(snapped, DurationMs)`.
  4. `_player.Advance(_clock.RideMs, nowUnix)` — applies the landed sample.
  5. `RideMs = _clock.RideMs`; fire **`Reset`** then `Ticked`.
- New event `event Action? Reset;` — raised on seek so views clear their
  visual state (the store data is already cleared in step 2; this clears the
  chart `DataLogger` and the map track overlay, which don't auto-clear).

### `TransportViewModel` (modify — `TelemetryPoc.App`)

- `bool IsPaused` (raises `PropertyChanged`) — reflects `_session.IsPaused`.
- `string PlayPauseGlyph => IsPaused ? "▶" : "⏸";` (▶ / ⏸).
- `void TogglePlayPause()` → `_session.Resume()` or `_session.Pause()`, then
  raise `IsPaused` + `PlayPauseGlyph`.
- `void Seek(double fraction)` → `_session.Seek(fraction)`.
- `Refresh()` (on `Ticked`) unchanged; also raise `IsPaused`/`PlayPauseGlyph`
  is **not** needed per-tick (only on toggle).

### `TransportBar.xaml` / `.xaml.cs` (modify — `TelemetryPoc.App`)

- Replace the static `⏮ ⏹ ⏭` `TextBlock` with a **play/pause `Button`** bound
  to `PlayPauseGlyph`, `Click` → `TogglePlayPause()` (flat INU style: no chrome,
  `TextDim` foreground, hover `TextData`).
- Make the `ProgressBar` (or its containing `Grid` cell) **click-to-seek**:
  a `MouseLeftButtonDown` handler computes `fraction = e.GetPosition(track).X / track.ActualWidth`,
  clamps `[0,1]`, calls `Seek(fraction)`. (Click only — no drag, mirrors Rust.)
- The `Value="{Binding Progress, Mode=OneWay}"` binding stays (read-only
  display; OneWay avoids the RangeBase TwoWay-write crash seen earlier).

### View reset wiring (modify — chart + map views)

- `LineChartView`: subscribe to `RideSession.Reset` (alongside its existing
  `Updated`); on `Reset`, clear the `DataLogger` and reset `_lastX` (the same
  clearing its `Detach` already does), so a backward seek wipes the cyan line.
- `MapWidgetView`: on `Reset`, clear/rebuild the `TrackOverlay` from the (now
  empty) GPS track so the track polyline restarts from the seek point. The
  cached basemap `SKPicture` is **not** rebuilt (region is static).

## Data flow

```
DispatcherTimer 33ms ─▶ RideSession.Tick
        │  delta=elapsed−lastElapsed
        ▼
   RideClock.Advance(delta×speed)   ── frozen while paused
        │  RideMs
        ▼
   ReplayPlayer.Advance(RideMs) ─▶ Store.ApplyFrame ─▶ views (Ticked)

play/pause Button ─▶ VM.TogglePlayPause ─▶ Session.Pause/Resume ─▶ Clock.Playing
progress click   ─▶ VM.Seek(frac) ─▶ Session.Seek
                         ├─ Store.ApplyMeta (reset)
                         ├─ Player.SeekTo(target) + snap + Advance
                         └─ Reset event ─▶ chart/map clear, refill forward
```

## Testing

- **`RideClock`** — xUnit (`TelemetryPoc.Core.Tests`): advance-while-playing
  accumulates; advance-while-paused is a no-op; negative/zero delta is a no-op;
  `SeekTo` clamps below 0, above duration, and mid-range; seek doesn't change
  `Playing`.
- **`ReplayPlayer`** — xUnit: `Advance(rideMs)` applies exactly the samples
  with `TsMs ≤ rideMs` (rewrite of existing tests to ride-ms); `SeekTo` lands
  `_next` on the first sample `≥ rideMs` (mid-range, before-first → 0,
  past-last → count); after `SeekTo` a forward `Advance` applies from the new
  index.
- **XAML / view wiring** — build-verified + launch-confirmed: play/pause
  freezes the clock + charts + map and toggles ▶/⏸; clicking the progress bar
  jumps the ride; after a backward seek the charts and map track clear and
  refill forward; the HUD FPS/frame-time keeps updating while paused.

## Non-goals

No live speed control (RIDE_SPEED env only, matching Rust). No drag-scrub
(click-to-seek only). No window backfill on seek (strip/chart/map refill
forward from the seek point, matching Rust's re-meta-on-seek). No EVENTS /
FLIGHT-TRACK tabs. No transport over a socket (the .NET app is in-process).
```
