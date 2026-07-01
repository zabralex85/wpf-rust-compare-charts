using TelemetryPoc.Application;
using TelemetryPoc.Infrastructure;

namespace TelemetryPoc.Tests;

public class RidePathsTests
{
    // A synthetic absolute root valid on the current OS. The old tests hardcoded
    // Windows literals (@"C:\repo\...") which don't parse as paths on Linux, so the
    // walk-up (DirectoryInfo/Path.Combine) produced separator-mismatched results and
    // the string asserts failed. Build every path from this root via Path.Combine so
    // the tests are separator-agnostic (green on Windows and Linux).
    private static readonly string Root =
        OperatingSystem.IsWindows() ? @"C:\poc-repo" : "/poc-repo";
    [Fact]
    public void Options_path_used_when_it_exists()
    {
        var opts = new RideOptions { DbPath = @"C:\rides\my.db" };
        var r = new RidePathResolver(opts, @"C:\app", _ => true).ResolveRideDb();
        Assert.Equal(@"C:\rides\my.db", r);
    }

    [Fact]
    public void Options_path_ignored_when_it_does_not_exist()
    {
        var opts = new RideOptions { DbPath = @"C:\rides\my.db" };
        // exists returns false for everything → walk-up finds nothing → absolute fallback
        var r = new RidePathResolver(opts, @"C:\nope", _ => false).ResolveRideDb();
        Assert.Equal(Path.Combine(@"C:\nope", "data", "ride.db"), r);
    }

    [Fact]
    public void Walks_up_for_data_ride_db()
    {
        // base dir <root>/dotnet/bin ; data/ride.db lives at <root>/data/ride.db
        var opts = new RideOptions();
        var baseDir = Path.Combine(Root, "dotnet", "bin");
        var hit = Path.Combine(Root, "data", "ride.db");
        var r = new RidePathResolver(opts, baseDir, p => p == hit).ResolveRideDb();
        Assert.Equal(hit, r);
    }

    [Fact]
    public void Prefers_ride_db_over_ride_small_db_in_same_dir()
    {
        var opts = new RideOptions();
        var baseDir = Path.Combine(Root, "dotnet");
        var rideDb = Path.Combine(Root, "data", "ride.db");
        var smallDb = Path.Combine(Root, "data", "ride_small.db");
        bool Exists(string p) => p == rideDb || p == smallDb;

        var r = new RidePathResolver(opts, baseDir, Exists).ResolveRideDb();
        Assert.Equal(rideDb, r);
    }

    [Fact]
    public void Falls_back_to_default_when_nothing_found()
    {
        var opts = new RideOptions();
        var r = new RidePathResolver(opts, @"C:\nope", _ => false).ResolveRideDb();
        Assert.Equal(Path.Combine(@"C:\nope", "data", "ride.db"), r);
    }
}
