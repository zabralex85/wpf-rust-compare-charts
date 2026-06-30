using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TelemetryPoc.Domain;

namespace TelemetryPoc.Application;

/// <summary>Loads an entire ride (channels, enums, samples, meta, GPS bounds).</summary>
public interface IRideSource
{
    Task<RideData> LoadAsync(CancellationToken ct = default);
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
