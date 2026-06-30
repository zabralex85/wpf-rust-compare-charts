using TelemetryPoc.Application;

namespace TelemetryPoc.Infrastructure;

public sealed class RidePathResolver : IRidePathResolver
{
    private readonly RideOptions _options;
    private readonly string _baseDir;
    private readonly Func<string, bool> _exists;

    public RidePathResolver(RideOptions options, string baseDir, Func<string, bool> exists)
    {
        _options = options;
        _baseDir = baseDir;
        _exists = exists;
    }

    public string ResolveRideDb()
    {
        if (!string.IsNullOrWhiteSpace(_options.DbPath) && _exists(_options.DbPath!)) return _options.DbPath!;
        return WalkUp("data", "ride.db", "ride_small.db") ?? Path.Combine(_baseDir, "data", "ride.db");
    }

    public string? ResolveMbTiles()
    {
        if (!string.IsNullOrWhiteSpace(_options.MbTilesPath) && _exists(_options.MbTilesPath!)) return _options.MbTilesPath;
        return WalkUp("tiles", "israel.mbtiles");
    }

    // Checks each candidate file within a directory before ascending (interleaved, matching the original).
    private string? WalkUp(string dir, params string[] files)
    {
        var d = new DirectoryInfo(_baseDir);
        while (d is not null)
        {
            foreach (var file in files)
            {
                var p = Path.Combine(d.FullName, dir, file);
                if (_exists(p)) return p;
            }
            d = d.Parent;
        }
        return null;
    }
}
