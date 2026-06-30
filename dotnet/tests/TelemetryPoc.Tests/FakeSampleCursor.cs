using TelemetryPoc.Application;
using TelemetryPoc.Domain;

namespace TelemetryPoc.Tests;

/// <summary>In-memory ISampleCursor for tests — mirrors the old list-backed player so
/// parity assertions hold without touching SQLite.</summary>
internal sealed class FakeSampleCursor : ISampleCursor
{
    private readonly IReadOnlyList<Sample> _samples;
    private int _next;

    public FakeSampleCursor(IReadOnlyList<Sample> samples)
    {
        _samples = samples;
    }

    public long? PeekTs => _next < _samples.Count ? _samples[_next].TsMs : null;

    public Sample Read() => _samples[_next++];

    public void SeekTo(long rideMs)
    {
        int lo = 0, hi = _samples.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (_samples[mid].TsMs < rideMs)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }
        _next = lo;
    }

    public void Dispose() { }
}
