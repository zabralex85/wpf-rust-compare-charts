using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using TelemetryPoc.App.Viz;
using TelemetryPoc.Core;

namespace TelemetryPoc.App;

public sealed class RideSession : INotifyPropertyChanged
{
    public TelemetryStore Store { get; } = new();
    private readonly MetricsSampler _metrics = new();
    private readonly Stopwatch _sw = new();
    private ReplayPlayer? _player;
    private DispatcherTimer? _timer;
    private double _speed = 1.0;
    private long _lastMetricSec = -1;

    private string _clockText = "00:00:00.000";
    public string ClockText { get => _clockText; private set { _clockText = value; Raise(nameof(ClockText)); } }

    private string _tplus = "T+00:00:00.000";
    public string TPlusText { get => _tplus; private set { _tplus = value; Raise(nameof(TPlusText)); } }

    public string? Error { get; private set; }

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
            Store.ApplyMeta(channels, enums);
            _player = new ReplayPlayer(samples, Store, _speed);

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
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _player?.Advance(elapsed, now);

        var rideMs = (long)(elapsed * _speed);
        var rideSec = rideMs / 1000;
        if (Store.LastEmitUnixMs > 0 && rideSec != _lastMetricSec)
        {
            _lastMetricSec = rideSec;
            Store.ApplyMetrics(_metrics.Sample());
        }

        ClockText = MissionClock.Format(rideMs);
        TPlusText = MissionClock.FormatTPlus(rideMs);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
