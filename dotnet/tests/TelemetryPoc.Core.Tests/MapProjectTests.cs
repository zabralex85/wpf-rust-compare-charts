using TelemetryPoc.Map;
using Xunit;

public class MapProjectTests
{
    [Fact]
    public void GpsToScreen_center_is_viewport_center()
    {
        var (x, y) = MapProject.GpsToScreen(new Region(32.08, 34.78, 12, 400, 300), 32.08, 34.78);
        Assert.Equal(200.0, x, 6); // Width/2
        Assert.Equal(150.0, y, 6); // Height/2
    }

    [Fact]
    public void GpsToScreen_east_is_right_of_center()
    {
        var r = new Region(32.08, 34.78, 12, 400, 300);
        var (x, _) = MapProject.GpsToScreen(r, 32.08, 34.79); // a touch east
        Assert.True(x > 200.0);
    }

    [Fact]
    public void TrackBounds_min_max()
    {
        var lat = new[] { 32.1, 32.0, 32.2 };
        var lon = new[] { 34.8, 34.7, 34.9 };
        var (minLat, minLon, maxLat, maxLon) = MapProject.TrackBounds(lat, lon);
        Assert.Equal(32.0, minLat, 6);
        Assert.Equal(34.7, minLon, 6);
        Assert.Equal(32.2, maxLat, 6);
        Assert.Equal(34.9, maxLon, 6);
    }
}
