using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using TelemetryPoc.App.Viz;
using TelemetryPoc.Core;
using TelemetryPoc.Map;

namespace TelemetryPoc.App;

public sealed class RideSession : INotifyPropertyChanged
{
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

    public long DurationMs { get; private set; }
    public long RideMs { get; private set; }
    public (double MinLat, double MinLon, double MaxLat, double MaxLon)? GpsBounds { get; private set; }

    private string _clockText = "00:00:00.000";
    public string ClockText { get => _clockText; private set { _clockText = value; Raise(nameof(ClockText)); } }

    private string _tplus = "T+00:00:00.000";
    public string TPlusText { get => _tplus; private set { _tplus = value; Raise(nameof(TPlusText)); } }

    public string? Error { get; private set; }

    public event Action? MetaLoaded;
    public event Action? Ticked;
    public event Action? Reset;
    public bool IsPaused => !_clock.Playing;

    public void Start()
    {
        try
        {
            var dbPath = RidePaths.Resolve(
                Environment.GetEnvironmentVariable("RIDE_DB"), AppContext.BaseDirectory, File.Exists);
            _speed = double.TryParse(Environment.GetEnvironmentVariable("RIDE_SPEED"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 1.0;

            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();
            var channels = TelemetryDb.LoadChannels(conn);
            var enums = TelemetryDb.LoadEnumValues(conn);
            var samples = TelemetryDb.LoadSamples(conn, channels);
            var meta = TelemetryDb.LoadRideMeta(conn);
            DurationMs = meta.DurationS * 1000;

            _channels = channels;
            _enums = enums;
            _samples = samples;

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

            Store.ApplyMeta(channels, enums);
            MetaLoaded?.Invoke();
            _player = new ReplayPlayer(samples, Store);

            _sw.Start();
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) => Tick();
            _timer.Start();
        }
        catch (Exception ex)
        {
            Error = $"DB load failed: {ex.Message}";
            Raise(nameof(Error));
        }
    }

    private void Tick()
    {
        var elapsed = _sw.ElapsedMilliseconds;
        var delta = elapsed - _lastElapsed;
        _lastElapsed = elapsed;
        // Paused: the clock is frozen and no data changes, so skip the whole per-tick
        // UI refresh. This lets WPF's compositor go idle (CPU drops to ~0) instead of
        // repainting the map/charts/gauges 30×/s for nothing — mirrors the Rust app,
        // which stops emitting frames (and thus stops re-rendering) while paused.
        if (!_clock.Playing) return;
        _clock.Advance((long)(delta * _speed));

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // The render timer runs at 30Hz to advance the clock smoothly, but the ride
        // data is only 10Hz. Repainting charts/map/gauges on every tick would redraw
        // identical data 2 of every 3 frames. Fire Ticked (the heavy redraw signal)
        // only when a new frame was actually emitted — ~3x fewer repaints, and it
        // mirrors Rust, which renders per frame-message rather than on a fixed clock.
        int applied = _player?.Advance(_clock.RideMs, now) ?? 0;

        var rideMs = _clock.RideMs;
        RideMs = rideMs;
        var rideSec = rideMs / 1000;
        if (Store.LastEmitUnixMs > 0 && rideSec != _lastMetricSec)
        {
            _lastMetricSec = rideSec;
            Store.ApplyMetrics(_metrics.Sample());
            applied++; // metrics changed (CPU/RAM) → refresh the HUD even between frames
        }

        // Clock text advances every tick so the mission clock stays smooth (cheap text).
        ClockText = MissionClock.Format(rideMs);
        TPlusText = MissionClock.FormatTPlus(rideMs);
        if (applied > 0) Ticked?.Invoke();
    }

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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
