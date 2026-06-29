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
