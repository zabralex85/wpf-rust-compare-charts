using System;
using TelemetryPoc.Core;

namespace TelemetryPoc.App.Viz;

/// <summary>UI-free replay orchestration: owns the store, replay player, ride clock and
/// the once-per-second metrics gate. Time is injected (wall-time delta + the current
/// unix ms), so it advances deterministically under test — no timer, stopwatch or
/// system clock inside. The WPF host (RideSession) supplies the time and the store.</summary>
public sealed class RideEngine
{
    private readonly RideData _data;
    private readonly RideClock _clock = new();
    private readonly MetricsSampler _metrics;
    private readonly ReplayPlayer _player;
    private long _lastMetricSec = -1;

    public TelemetryStore Store { get; }
    public long DurationMs => _data.DurationMs;
    public long RideMs { get; private set; }
    public (double MinLat, double MinLon, double MaxLat, double MaxLon)? GpsBounds => _data.GpsBounds;
    public bool IsPaused => !_clock.Playing;

    /// <summary>Raised on seek, after the store is re-meta'd — views clear their buffers.</summary>
    public event Action? Reset;

    public RideEngine(RideData data, TelemetryStore store, MetricsSampler? metrics = null)
    {
        _data = data;
        Store = store;
        _metrics = metrics ?? new MetricsSampler();
        Store.ApplyMeta(data.Channels, data.Enums);
        _player = new ReplayPlayer(data.Samples, Store);
    }

    /// <summary>Advance the clock by a wall-time delta (scaled by speed) and apply any
    /// samples now due. Returns true when the UI should repaint — a new frame was emitted
    /// or a fresh per-second metrics sample landed — and false on an idle/paused tick, so
    /// the 30 Hz host loop only repaints at the 10 Hz data cadence.</summary>
    public bool Advance(long wallDeltaMs, long nowUnixMs, double speed = 1.0)
    {
        if (!_clock.Playing) return false;
        _clock.Advance((long)(wallDeltaMs * speed));
        int applied = _player.Advance(_clock.RideMs, nowUnixMs);
        RideMs = _clock.RideMs;

        bool metricsChanged = false;
        var rideSec = RideMs / 1000;
        if (Store.LastEmitUnixMs > 0 && rideSec != _lastMetricSec)
        {
            _lastMetricSec = rideSec;
            Store.ApplyMetrics(_metrics.Sample());
            metricsChanged = true;
        }
        return applied > 0 || metricsChanged;
    }

    public void Pause() => _clock.Pause();
    public void Resume() => _clock.Resume();

    public void Seek(double fraction, long nowUnixMs)
    {
        Store.ApplyMeta(_data.Channels, _data.Enums);
        var target = (long)(Math.Clamp(fraction, 0, 1) * DurationMs);
        _player.SeekTo(target);
        var snapped = _player.PeekTs ?? target;
        _clock.SeekTo(snapped, DurationMs);
        _player.Advance(_clock.RideMs, nowUnixMs);
        RideMs = _clock.RideMs;
        _lastMetricSec = -1;
        Reset?.Invoke();
    }
}
