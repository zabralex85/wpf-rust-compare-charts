using System;
using System.IO;
using TelemetryPoc.Application;

namespace TelemetryPoc.Infrastructure;

public sealed class RidePathResolver : IRidePathResolver
{
    private readonly Func<string, bool> _exists;
    private readonly string _baseDir;

    public RidePathResolver(string baseDir, Func<string, bool> exists)
    {
        _baseDir = baseDir;
        _exists = exists;
    }

    public string ResolveRideDb()
    {
        var env = Environment.GetEnvironmentVariable("RIDE_DB");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        return WalkUp("data", "ride.db") ?? WalkUp("data", "ride_small.db")
            ?? Path.Combine(_baseDir, "data", "ride.db");
    }

    public string? ResolveMbTiles()
    {
        var env = Environment.GetEnvironmentVariable("RIDE_MBTILES");
        if (!string.IsNullOrWhiteSpace(env) && _exists(env)) return env;
        return WalkUp("tiles", "israel.mbtiles");
    }

    private string? WalkUp(string dir, string file)
    {
        var d = new DirectoryInfo(_baseDir);
        while (d is not null)
        {
            var p = Path.Combine(d.FullName, dir, file);
            if (_exists(p)) return p;
            d = d.Parent;
        }
        return null;
    }
}
