using TelemetryPoc.Application;
using TelemetryPoc.Infrastructure;

namespace TelemetryPoc.Tests;

/// <summary>Covers RidePathResolver.ResolveMbTiles, which RidePathsTests.cs doesn't touch
/// (that file only exercises ResolveRideDb).</summary>
public class RidePathResolverMbTilesTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\poc-repo" : "/poc-repo";

    [Fact]
    public void Options_mbtiles_path_used_when_it_exists()
    {
        var opts = new RideOptions { MbTilesPath = @"C:\tiles\israel.mbtiles" };
        var r = new RidePathResolver(opts, @"C:\app", _ => true).ResolveMbTiles();
        Assert.Equal(@"C:\tiles\israel.mbtiles", r);
    }

    [Fact]
    public void Options_mbtiles_path_ignored_when_it_does_not_exist_and_walks_up()
    {
        var opts = new RideOptions { MbTilesPath = @"C:\tiles\israel.mbtiles" };
        var baseDir = Path.Combine(Root, "dotnet");
        var hit = Path.Combine(Root, "tiles", "israel.mbtiles");
        var r = new RidePathResolver(opts, baseDir, p => p == hit).ResolveMbTiles();
        Assert.Equal(hit, r);
    }

    [Fact]
    public void ResolveMbTiles_returns_null_when_nothing_found()
    {
        var opts = new RideOptions();
        var r = new RidePathResolver(opts, Path.Combine(Root, "nope"), _ => false).ResolveMbTiles();
        Assert.Null(r);
    }

    [Fact]
    public void ResolveMbTiles_uses_default_options_when_path_unset()
    {
        var opts = new RideOptions(); // MbTilesPath is null
        var baseDir = Path.Combine(Root, "dotnet");
        var hit = Path.Combine(Root, "tiles", "israel.mbtiles");
        var r = new RidePathResolver(opts, baseDir, p => p == hit).ResolveMbTiles();
        Assert.Equal(hit, r);
    }
}
