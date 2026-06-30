using SkiaSharp;

namespace TelemetryPoc.Map;

public static class TrackOverlay
{
    public static void Draw(SKCanvas canvas, Region region, IReadOnlyList<double> lat, IReadOnlyList<double> lon)
    {
        int n = Math.Min(lat.Count, lon.Count);
        if (n == 0) return;

        using var line = new SKPaint
        {
	        IsAntialias = true,
	        Color = SKColor.Parse("#38c5e0"),
	        Style = SKPaintStyle.Stroke,
	        StrokeWidth = 3,
	        StrokeCap = SKStrokeCap.Round,
	        StrokeJoin = SKStrokeJoin.Round
        };

        using var path = new SKPath();
        for (int i = 0; i < n; i++)
        {
            var (x, y) = MapProject.GpsToScreen(region, lat[i], lon[i]);
            if (i == 0) path.MoveTo((float)x, (float)y);
            else path.LineTo((float)x, (float)y);
        }
        canvas.DrawPath(path, line);

        using var marker = new SKPaint
        {
	        IsAntialias = true,
	        Color = SKColor.Parse("#2fd17a"),
	        Style = SKPaintStyle.Fill
        };

        var (mx, my) = MapProject.GpsToScreen(region, lat[n - 1], lon[n - 1]);
        canvas.DrawCircle((float)mx, (float)my, 5, marker);
    }
}
