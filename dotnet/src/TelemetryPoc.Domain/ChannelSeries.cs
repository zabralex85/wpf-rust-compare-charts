namespace TelemetryPoc.Domain;

public sealed class ChannelSeries
{
    private readonly long _windowMs;
    private readonly List<long> _xs = [];
    private readonly List<double> _ys = [];

    public ChannelSeries(long windowMs)
    {
        _windowMs = windowMs;
    }

    public void Push(long tsMs, double value)
    {
        _xs.Add(tsMs);
        _ys.Add(value);
        var cutoff = tsMs - _windowMs;
        int drop = 0;
        while (drop < _xs.Count && _xs[drop] < cutoff)
        {
            drop++;
        }

        if (drop > 0)
        {
            _xs.RemoveRange(0, drop);
            _ys.RemoveRange(0, drop);
        }
    }

    public (IReadOnlyList<long> Xs, IReadOnlyList<double> Ys) Arrays() => (_xs, _ys);

    public int Len => _xs.Count;
}
