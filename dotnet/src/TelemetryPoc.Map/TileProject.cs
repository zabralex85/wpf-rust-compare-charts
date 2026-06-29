namespace TelemetryPoc.Map;

public static class TileProject
{
    private const double TileSize = 256.0;

    public static (double X, double Y) ToScreen(double tileScreenX, double tileScreenY, long localX, long localY, int extent)
    {
        var fx = extent == 0 ? 0 : localX / (double)extent;
        var fy = extent == 0 ? 0 : localY / (double)extent;
        return (tileScreenX + fx * TileSize, tileScreenY + fy * TileSize);
    }
}
