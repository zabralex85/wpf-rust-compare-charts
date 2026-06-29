namespace TelemetryPoc.Map;

public sealed record TileRef(int Z, int X, int Y, double ScreenX, double ScreenY, double PixSize = 256.0);

public static class TileMath
{
    public const int MinZoom = 7;          // zoom out far enough to see the whole region
    public const int MaxZoom = 14;         // highest zoom the mbtiles actually has data for
    public const int MaxDisplayZoom = 18;  // allow over-zoom past the data (z14 tiles scaled up)
    private const double TileSize = 256.0;

    public static IReadOnlyList<TileRef> VisibleTiles(Region r)
    {
        // Over-zoom: past MaxZoom there are no tiles, so fetch z=MaxZoom tiles and draw
        // each scaled up by 2^(displayZoom-MaxZoom) — MapLibre-style "zoom to street".
        int tileZoom = Math.Min(r.Zoom, MaxZoom);
        double pix = TileSize * Math.Pow(2, r.Zoom - tileZoom); // on-screen size of one data tile

        var (cx, cy) = WebMercator.LonLatToWorld(r.CenterLon, r.CenterLat, r.Zoom);
        var left = cx - r.Width / 2;
        var top = cy - r.Height / 2;
        var maxIndex = (1 << tileZoom) - 1;

        var x0 = (int)Math.Floor(left / pix);
        var x1 = (int)Math.Floor((left + r.Width) / pix);
        var y0 = (int)Math.Floor(top / pix);
        var y1 = (int)Math.Floor((top + r.Height) / pix);

        var tiles = new List<TileRef>();
        for (int ty = y0; ty <= y1; ty++)
        {
            if (ty < 0 || ty > maxIndex) continue;
            for (int tx = x0; tx <= x1; tx++)
            {
                if (tx < 0 || tx > maxIndex) continue;
                tiles.Add(new TileRef(tileZoom, tx, ty, tx * pix - left, ty * pix - top, pix));
            }
        }
        return tiles;
    }

    public static (double CenterLat, double CenterLon, int Zoom) FitBbox(
        double minLat, double minLon, double maxLat, double maxLon, double width, double height)
    {
        var centerLat = (minLat + maxLat) / 2;
        var centerLon = (minLon + maxLon) / 2;
        for (int z = MaxZoom; z >= MinZoom; z--)
        {
            var (x0, y0) = WebMercator.LonLatToWorld(minLon, maxLat, z); // NW
            var (x1, y1) = WebMercator.LonLatToWorld(maxLon, minLat, z); // SE
            var dx = Math.Abs(x1 - x0);
            var dy = Math.Abs(y1 - y0);
            if (dx <= width * 0.9 && dy <= height * 0.9) return (centerLat, centerLon, z);
        }
        return (centerLat, centerLon, MinZoom);
    }
}
