namespace TelemetryPoc.Map;

public static class WebMercator
{
    private const double TileSize = 256.0;
    private const double MaxLat = 85.05112878;

    public static double MapSize(int z) => TileSize * Math.Pow(2, z);

    public static (double X, double Y) LonLatToWorld(double lon, double lat, int z)
    {
        var size = MapSize(z);
        var clampedLat = Math.Max(-MaxLat, Math.Min(MaxLat, lat));
        var x = (lon + 180.0) / 360.0 * size;
        var sin = Math.Sin(clampedLat * Math.PI / 180.0);
        var y = Math.Clamp((0.5 - Math.Log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * size, 0.0, size);
        return (x, y);
    }

    public static (double Lon, double Lat) WorldToLonLat(double worldX, double worldY, int z)
    {
        var size = MapSize(z);
        var lon = worldX / size * 360.0 - 180.0;
        var yNorm = worldY / size;
        var lat = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * yNorm))) * 180.0 / Math.PI;
        return (lon, lat);
    }

    public static (int X, int Y) TileXY(double lon, double lat, int z)
    {
        var (x, y) = LonLatToWorld(lon, lat, z);
        return ((int)Math.Floor(x / TileSize), (int)Math.Floor(y / TileSize));
    }
}
