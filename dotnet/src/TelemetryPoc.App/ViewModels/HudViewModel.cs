using System.ComponentModel;
using System.Globalization;
using TelemetryPoc.Presentation;

namespace TelemetryPoc.App.ViewModels;

public sealed class HudViewModel : INotifyPropertyChanged
{
    private readonly RideSession _session;
    private readonly FpsMeter _fps = new();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public HudViewModel(RideSession session)
    {
        _session = session;
        _session.Ticked += OnTick;
    }

    private string _fpsText = "—", _frameText = "—", _latencyText = "—", _cpuText = "—", _ramText = "—";
    public string FpsText { get => _fpsText; private set { _fpsText = value; Raise(nameof(FpsText)); } }
    public string FrameText { get => _frameText; private set { _frameText = value; Raise(nameof(FrameText)); } }
    public string LatencyText { get => _latencyText; private set { _latencyText = value; Raise(nameof(LatencyText)); } }
    public string CpuText { get => _cpuText; private set { _cpuText = value; Raise(nameof(CpuText)); } }
    public string RamText { get => _ramText; private set { _ramText = value; Raise(nameof(RamText)); } }

    private bool _wasPaused;

    public void Tick(TimeSpan elapsed)
    {
        // While paused the scene is static; updating the FPS text every frame would itself
        // keep the compositor (and CPU) busy. Skip it so the app idles when paused.
        if (_session.IsPaused) { _wasPaused = true; return; }
        // On the first frame after resume, drop the pause-gap sample: elapsed kept advancing
        // while paused, so the next interval would otherwise be ~pauseDuration and skew FPS for ~60 frames.
        if (_wasPaused) { _fps.Reset(); _wasPaused = false; }
        _fps.Tick(elapsed.TotalMilliseconds);
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
