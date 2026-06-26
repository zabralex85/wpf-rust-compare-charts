namespace TelemetryPoc.Core;

public sealed class ReplayPlayer
{
    private readonly IReadOnlyList<Sample> _samples;
    private readonly TelemetryStore _store;
    private readonly Pacer _pacer;
    private int _next;

    public ReplayPlayer(IReadOnlyList<Sample> samples, TelemetryStore store, double speed)
    {
        _samples = samples;
        _store = store;
        _pacer = new Pacer(speed);
    }

    public bool Done => _next >= _samples.Count;

    public int Advance(long elapsedMs, long nowUnixMs)
    {
        int applied = 0;
        while (_next < _samples.Count && _pacer.DueOffsetMs(_samples[_next].TsMs) <= elapsedMs)
        {
            _store.ApplyFrame(_samples[_next], nowUnixMs);
            _next++;
            applied++;
        }
        return applied;
    }
}
