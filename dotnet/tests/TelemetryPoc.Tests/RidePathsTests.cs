using TelemetryPoc.Application;
using TelemetryPoc.Infrastructure;

namespace TelemetryPoc.Tests;

public class RidePathsTests
{
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
        // base dir C:\repo\dotnet\bin ; data\ride.db lives at C:\repo\data\ride.db
        var opts = new RideOptions();
        var hit = @"C:\repo\data\ride.db";
        var r = new RidePathResolver(opts, @"C:\repo\dotnet\bin", p => p == hit).ResolveRideDb();
        Assert.Equal(hit, r);
    }

    [Fact]
    public void Prefers_ride_db_over_ride_small_db_in_same_dir()
    {
        var opts = new RideOptions();
        static bool Exists(string p)
        {
            return p == @"C:\repo\data\ride.db" || p == @"C:\repo\data\ride_small.db";
        }

        var r = new RidePathResolver(opts, @"C:\repo\dotnet", Exists).ResolveRideDb();
        Assert.Equal(@"C:\repo\data\ride.db", r);
    }

    [Fact]
    public void Falls_back_to_default_when_nothing_found()
    {
        var opts = new RideOptions();
        var r = new RidePathResolver(opts, @"C:\nope", _ => false).ResolveRideDb();
        Assert.Equal(Path.Combine(@"C:\nope", "data", "ride.db"), r);
    }
}
