namespace TelemetryPoc.Map;

public static class MapProject
{
    public static (double X, double Y) GpsToScreen(Region r, double lat, double lon)
    {
        var (wx, wy) = WebMercator.LonLatToWorld(lon, lat, r.Zoom);
        var (cx, cy) = WebMercator.LonLatToWorld(r.CenterLon, r.CenterLat, r.Zoom);
        return (wx - cx + r.Width / 2, wy - cy + r.Height / 2);
    }

    public static (double MinLat, double MinLon, double MaxLat, double MaxLon) TrackBounds(
        IReadOnlyList<double> lat, IReadOnlyList<double> lon)
    {
        double minLat = double.MaxValue, minLon = double.MaxValue, maxLat = double.MinValue, maxLon = double.MinValue;
        int n = Math.Min(lat.Count, lon.Count);
        for (int i = 0; i < n; i++)
        {
            if (lat[i] < minLat) minLat = lat[i];
            if (lat[i] > maxLat) maxLat = lat[i];
            if (lon[i] < minLon) minLon = lon[i];
            if (lon[i] > maxLon) maxLon = lon[i];
        }
        return (minLat, minLon, maxLat, maxLon);
    }
}
