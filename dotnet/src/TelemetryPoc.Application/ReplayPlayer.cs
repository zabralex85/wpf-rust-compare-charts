using TelemetryPoc.Domain;

namespace TelemetryPoc.Application;

public sealed class ReplayPlayer
{
    private readonly IReadOnlyList<Sample> _samples;
    private readonly TelemetryStore _store;
    private int _next;

    public ReplayPlayer(IReadOnlyList<Sample> samples, TelemetryStore store)
    {
        _samples = samples;
        _store = store;
    }

    public bool Done => _next >= _samples.Count;

    /// <summary>TsMs of the sample at the playhead, or null if past the end.</summary>
    public long? PeekTs => _next < _samples.Count ? _samples[_next].TsMs : null;

    /// <summary>Apply every sample whose TsMs is at or before the ride clock (ms from ride start).</summary>
    public int Advance(long rideMs, long nowUnixMs)
    {
        int applied = 0;
        while (_next < _samples.Count && _samples[_next].TsMs <= rideMs)
        {
            _store.ApplyFrame(_samples[_next], nowUnixMs);
            _next++;
            applied++;
        }
        return applied;
    }

    /// <summary>Move the playhead to the first sample with TsMs &gt;= rideMs (lower bound). Returns the new index.</summary>
    public int SeekTo(long rideMs)
    {
        int lo = 0, hi = _samples.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (_samples[mid].TsMs < rideMs) lo = mid + 1;
            else hi = mid;
        }
        _next = lo;
        return _next;
    }
}
