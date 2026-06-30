using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using TelemetryPoc.App.Viz;
using TelemetryPoc.Core;
using TelemetryPoc.Map;

namespace TelemetryPoc.App;

/// <summary>WPF host for the replay. Loads the ride DB, drives a <see cref="RideEngine"/>
/// from a 30 Hz dispatcher timer (wall-clock delta in, repaint signal out) on the UI
/// thread, and exposes the engine state to the view-models. All replay logic lives in the
/// UI-free engine; this class only owns the timer, the wall clock and the IO.</summary>
public sealed class RideSession
{
    private readonly Stopwatch _sw = new();
    private RideEngine? _engine;
    private DispatcherTimer? _timer;
    private double _speed = 1.0;
    private long _lastElapsed;

    public TelemetryStore Store { get; } = new();
    public long DurationMs => _engine?.DurationMs ?? 0;
    public long RideMs => _engine?.RideMs ?? 0;
    public (double MinLat, double MinLon, double MaxLat, double MaxLon)? GpsBounds => _engine?.GpsBounds;
    public bool IsPaused => _engine?.IsPaused ?? true;
    public string? Error { get; private set; }

    public event Action? MetaLoaded;
    public event Action? Ticked;
    public event Action? Reset;

    public void Start()
    {
        try
        {
            var dbPath = RidePaths.Resolve(
                Environment.GetEnvironmentVariable("RIDE_DB"), AppContext.BaseDirectory, File.Exists);
            _speed = double.TryParse(Environment.GetEnvironmentVariable("RIDE_SPEED"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 1.0;

            _engine = new RideEngine(LoadRide(dbPath), Store);
            _engine.Reset += () => Reset?.Invoke();
            MetaLoaded?.Invoke();

            _sw.Start();
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) => OnTick();
            _timer.Start();
        }
        catch (Exception ex)
        {
            Error = $"DB load failed: {ex.Message}";
        }
    }

    private void OnTick()
    {
        var elapsed = _sw.ElapsedMilliseconds;
        var delta = elapsed - _lastElapsed;
        _lastElapsed = elapsed;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (_engine!.Advance(delta, now, _speed)) Ticked?.Invoke();
    }

    public void Pause() => _engine?.Pause();
    public void Resume() => _engine?.Resume();

    public void Seek(double fraction)
    {
        if (_engine is null) return;
        _engine.Seek(fraction, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Ticked?.Invoke();
    }

    private static RideData LoadRide(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        var channels = TelemetryDb.LoadChannels(conn);
        var enums = TelemetryDb.LoadEnumValues(conn);
        var samples = TelemetryDb.LoadSamples(conn, channels);
        var meta = TelemetryDb.LoadRideMeta(conn);
        return new RideData(channels, enums, samples, meta.DurationS * 1000, GpsBoundsOf(channels, samples));
    }

    private static (double, double, double, double)? GpsBoundsOf(
        IReadOnlyList<ChannelMeta> channels, IReadOnlyList<Sample> samples)
    {
        int latIdx = -1, lonIdx = -1;
        for (int i = 0; i < channels.Count; i++)
        {
            if (channels[i].Widget == "map_lat") latIdx = i;
            if (channels[i].Widget == "map_lon") lonIdx = i;
        }
        if (latIdx < 0 || lonIdx < 0 || samples.Count == 0) return null;

        var lat = new double[samples.Count];
        var lon = new double[samples.Count];
        for (int i = 0; i < samples.Count; i++) { lat[i] = samples[i].Values[latIdx]; lon[i] = samples[i].Values[lonIdx]; }
        return MapProject.TrackBounds(lat, lon);
    }
}
