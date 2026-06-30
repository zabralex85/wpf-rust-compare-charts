using TelemetryPoc.Domain;

namespace TelemetryPoc.Application;

/// <summary>Drives the store from a forward sample cursor: each Advance applies every
/// row now due (ts &lt;= rideMs). The cursor streams rows, so the whole ride is never in memory.</summary>
public sealed class ReplayPlayer
{
    private readonly ISampleCursor _cursor;
    private readonly TelemetryStore _store;

    public ReplayPlayer(ISampleCursor cursor, TelemetryStore store)
    {
        _cursor = cursor;
        _store = store;
    }

    public bool Done => _cursor.PeekTs is null;

    public long? PeekTs => _cursor.PeekTs;

    /// <summary>Apply every sample whose TsMs is at or before the ride clock. Returns the count applied.</summary>
    public int Advance(long rideMs, long nowUnixMs)
    {
        int applied = 0;
        while (_cursor.PeekTs is { } t && t <= rideMs)
        {
            _store.ApplyFrame(_cursor.Read(), nowUnixMs);
            applied++;
        }
        return applied;
    }

    /// <summary>Reposition to the first sample with TsMs &gt;= rideMs.</summary>
    public void SeekTo(long rideMs) => _cursor.SeekTo(rideMs);
}
