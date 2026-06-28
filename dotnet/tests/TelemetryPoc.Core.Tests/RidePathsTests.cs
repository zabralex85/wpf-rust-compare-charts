using TelemetryPoc.App.Viz;
using Xunit;

public class RidePathsTests
{
    [Fact]
    public void Env_path_used_when_it_exists()
    {
        var r = RidePaths.Resolve(@"C:\rides\my.db", @"C:\app", p => p == @"C:\rides\my.db");
        Assert.Equal(@"C:\rides\my.db", r);
    }

    [Fact]
    public void Walks_up_for_data_ride_db()
    {
        // base dir C:\repo\dotnet\bin ; data\ride.db lives at C:\repo\data\ride.db
        var hit = @"C:\repo\data\ride.db";
        var r = RidePaths.Resolve(null, @"C:\repo\dotnet\bin", p => p == hit);
        Assert.Equal(hit, r);
    }

    [Fact]
    public void Prefers_ride_db_over_ride_small_db_in_same_dir()
    {
        bool Exists(string p) => p == @"C:\repo\data\ride.db" || p == @"C:\repo\data\ride_small.db";
        var r = RidePaths.Resolve(null, @"C:\repo\dotnet", Exists);
        Assert.Equal(@"C:\repo\data\ride.db", r);
    }

    [Fact]
    public void Falls_back_to_default_when_nothing_found()
    {
        var r = RidePaths.Resolve(null, @"C:\nope", _ => false);
        Assert.Equal(Path.Combine("data", "ride.db"), r);
    }
}
