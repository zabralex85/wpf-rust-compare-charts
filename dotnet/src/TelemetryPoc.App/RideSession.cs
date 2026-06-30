// dotnet/src/TelemetryPoc.App/RideSession.cs

using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using TelemetryPoc.Application;
using TelemetryPoc.Domain;

namespace TelemetryPoc.App;

/// <summary>WPF host for the replay: loads the ride via IRideSource (off the UI thread),
/// drives a RideEngine from a 30 Hz dispatcher timer, and exposes the engine state to the
/// view-models. Owns the timer + wall clock + lifecycle; all replay logic is in the engine.</summary>
public sealed class RideSession : IDisposable
{
    private readonly IRideSource _source;
    private readonly IMetricsSampler _metrics;
    private readonly ISystemClock _clock;
    private readonly ILogger<RideSession> _log;
    private readonly Stopwatch _sw = new();

    private RideEngine? _engine;
    private DispatcherTimer? _timer;
    private double _speed;
    private long _lastElapsed;

    public RideSession(IRideSource source, IMetricsSampler metrics, ISystemClock clock,
        ILogger<RideSession> log, RideOptions options)
    {
        _source = source;
        _metrics = metrics;
        _clock = clock;
        _log = log;
        _speed = options.Speed;
    }

    public TelemetryStore Store { get; } = new();
    public long DurationMs => _engine?.DurationMs ?? 0;
    public long RideMs => _engine?.RideMs ?? 0;
    public (double MinLat, double MinLon, double MaxLat, double MaxLon)? GpsBounds => _engine?.GpsBounds;
    public bool IsPaused => _engine?.IsPaused ?? true;

    private string? _error;
    public string? Error { get => _error; private set { _error = value; ErrorChanged?.Invoke(); } }

    public event Action? MetaLoaded;
    public event Action? Ticked;
    public event Action? Reset;
    public event Action? ErrorChanged;

    public async void StartAsync()
    {
        try
        {
            _log.LogInformation("Loading ride…");
            var data = await _source.LoadAsync().ConfigureAwait(true); // resume on UI thread
            var cursor = await Task.Run(() => _source.OpenSamples()).ConfigureAwait(true);
            _log.LogInformation("Ride loaded: {Channels} channels, {DurationMs} ms", data.Channels.Count, data.DurationMs);

            _engine = new RideEngine(data, cursor, Store, _metrics);
            _engine.Reset += () => Reset?.Invoke();
            MetaLoaded?.Invoke();

            _sw.Start();
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) => OnTick();
            _timer.Start();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ride load failed");
            Error = $"Ride load failed: {ex.Message}";
        }
    }

    private void OnTick()
    {
        var elapsed = _sw.ElapsedMilliseconds;
        var delta = elapsed - _lastElapsed;
        _lastElapsed = elapsed;

        if (_engine!.Advance(delta, _clock.UtcNowUnixMs, _speed))
        {
            Ticked?.Invoke();
        }
    }

    public void Pause() => _engine?.Pause();
    public void Resume() => _engine?.Resume();

    public void Seek(double fraction)
    {
        if (_engine is null)
        {
            return;
        }

        _engine.Seek(fraction, _clock.UtcNowUnixMs);
        Ticked?.Invoke();
    }

    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
        _engine?.Dispose();
    }
}
