using SkiaSharp;
using TelemetryPoc.Domain;
using TelemetryPoc.Presentation;

namespace TelemetryPoc.Tests;

public class TrackOverlayTests
{
    private static Region Region() => new(32.08, 34.78, 12, 200, 200);

    [Fact]
    public void Draws_a_line_and_marker_for_a_multi_point_track()
    {
        using var surface = SKSurface.Create(new SKImageInfo(200, 200));
        surface.Canvas.Clear(SKColors.Black);

        var lat = new[] { 32.08, 32.081, 32.082 };
        var lon = new[] { 34.78, 34.781, 34.782 };

        var ex = Record.Exception(() => TrackOverlay.Draw(surface.Canvas, Region(), lat, lon));
        Assert.Null(ex);

        using var image = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(image);
        bool sawNonBlack = false;
        for (int y = 0; y < bmp.Height && !sawNonBlack; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                if (bmp.GetPixel(x, y) != SKColors.Black)
                {
                    sawNonBlack = true;
                    break;
                }
            }
        }

        Assert.True(sawNonBlack, "expected the track line/marker to paint over the cleared background");
    }

    [Fact]
    public void Single_point_draws_only_the_marker()
    {
        using var surface = SKSurface.Create(new SKImageInfo(100, 100));
        surface.Canvas.Clear(SKColors.Black);

        var ex = Record.Exception(() => TrackOverlay.Draw(surface.Canvas, Region(), [32.08], [34.78]));
        Assert.Null(ex);
    }

    [Fact]
    public void Mismatched_length_lists_use_the_shorter_count()
    {
        using var surface = SKSurface.Create(new SKImageInfo(100, 100));
        var ex = Record.Exception(() => TrackOverlay.Draw(surface.Canvas, Region(), [32.08, 32.081, 32.082], [34.78]));
        Assert.Null(ex);
    }

    [Fact]
    public void Empty_track_draws_nothing_and_does_not_throw()
    {
        using var surface = SKSurface.Create(new SKImageInfo(50, 50));
        surface.Canvas.Clear(SKColors.Black);

        TrackOverlay.Draw(surface.Canvas, Region(), [], []);

        using var image = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(image);
        Assert.Equal(SKColors.Black, bmp.GetPixel(0, 0)); // untouched
    }
}
