using TelemetryPoc.Domain;

namespace TelemetryPoc.Application;

/// <summary>Adapts IReadOnlyList&lt;Sample&gt; to the ISampleCursor interface.</summary>
internal sealed class ListSampleCursor : ISampleCursor
{
    private readonly IReadOnlyList<Sample> _samples;
    private int _next;

    public ListSampleCursor(IReadOnlyList<Sample> samples) => _samples = samples;

    public long? PeekTs => _next < _samples.Count ? _samples[_next].TsMs : null;

    public Sample Read() => _samples[_next++];

    public void SeekTo(long rideMs)
    {
        int lo = 0, hi = _samples.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (_samples[mid].TsMs < rideMs) lo = mid + 1;
            else hi = mid;
        }
        _next = lo;
    }

    public void Dispose() { }
}
