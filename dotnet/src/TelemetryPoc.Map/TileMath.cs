namespace TelemetryPoc.Map;

public sealed record TileRef(int Z, int X, int Y, double ScreenX, double ScreenY);

public static class TileMath
{
    private const double TileSize = 256.0;

    public static IReadOnlyList<TileRef> VisibleTiles(Region r)
    {
        var (cx, cy) = WebMercator.LonLatToWorld(r.CenterLon, r.CenterLat, r.Zoom);
        var left = cx - r.Width / 2;
        var top = cy - r.Height / 2;
        var maxIndex = (1 << r.Zoom) - 1;

        var x0 = (int)Math.Floor(left / TileSize);
        var x1 = (int)Math.Floor((left + r.Width) / TileSize);
        var y0 = (int)Math.Floor(top / TileSize);
        var y1 = (int)Math.Floor((top + r.Height) / TileSize);

        var tiles = new List<TileRef>();
        for (int ty = y0; ty <= y1; ty++)
        {
            if (ty < 0 || ty > maxIndex) continue;
            for (int tx = x0; tx <= x1; tx++)
            {
                if (tx < 0 || tx > maxIndex) continue;
                tiles.Add(new TileRef(r.Zoom, tx, ty, tx * TileSize - left, ty * TileSize - top));
            }
        }
        return tiles;
    }

    public static (double CenterLat, double CenterLon, int Zoom) FitBbox(
        double minLat, double minLon, double maxLat, double maxLon, double width, double height)
    {
        var centerLat = (minLat + maxLat) / 2;
        var centerLon = (minLon + maxLon) / 2;
        for (int z = 14; z >= 9; z--)
        {
            var (x0, y0) = WebMercator.LonLatToWorld(minLon, maxLat, z); // NW
            var (x1, y1) = WebMercator.LonLatToWorld(maxLon, minLat, z); // SE
            var dx = Math.Abs(x1 - x0);
            var dy = Math.Abs(y1 - y0);
            if (dx <= width * 0.9 && dy <= height * 0.9) return (centerLat, centerLon, z);
        }
        return (centerLat, centerLon, 9);
    }
}
