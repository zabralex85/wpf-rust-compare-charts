using TelemetryPoc.Domain;

namespace TelemetryPoc.Application;

/// <summary>Loads ride metadata (channels, enums, duration, GPS bounds) and opens a
/// forward sample cursor. Samples are streamed, never fully materialised.</summary>
public interface IRideSource
{
    Task<RideData> LoadAsync(CancellationToken ct = default);

    /// <summary>Open a fresh forward cursor over the ride's samples (ts-ascending).
    /// The caller owns and disposes it.</summary>
    ISampleCursor OpenSamples();
}

/// <summary>Forward, seekable cursor over ride samples — streamed from storage so the
/// whole ride is never held in memory.</summary>
public interface ISampleCursor : IDisposable
{
    /// <summary>TsMs of the row at the playhead, or null once past the last row.</summary>
    long? PeekTs { get; }

    /// <summary>Return the row at the playhead and advance one. Call only when PeekTs is non-null.</summary>
    Sample Read();

    /// <summary>Reposition the playhead to the first row with TsMs &gt;= rideMs.</summary>
    void SeekTo(long rideMs);
}

/// <summary>One CPU%/RAM sample of the current process, matching the Rust app's sysinfo.</summary>
public interface IMetricsSampler
{
    Metrics Sample();
}

/// <summary>The wall clock, injectable so latency/emit timestamps are testable.</summary>
public interface ISystemClock
{
    long UtcNowUnixMs { get; }
}

/// <summary>Offline basemap tiles: read + gunzip + MVT-decode, memoised.</summary>
public interface ITileSource : IDisposable
{
    IReadOnlyList<MapFeature>? Decoded(int z, int x, int y);
}

/// <summary>Resolves the ride DB and mbtiles paths from configuration + fallbacks.</summary>
public interface IRidePathResolver
{
    string ResolveRideDb();
    string? ResolveMbTiles();
}
