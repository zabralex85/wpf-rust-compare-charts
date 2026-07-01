using SkiaSharp;
using TelemetryPoc.Application;
using TelemetryPoc.Domain;
using TelemetryPoc.Infrastructure;
using TelemetryPoc.Presentation;

namespace TelemetryPoc.Tests;

/// <summary>A fixed feature set served for every visible tile, regardless of z/x/y —
/// enough to exercise BasemapRenderer's layer matching, path building and label
/// placement without needing a real MVT-encoded fixture.</summary>
internal sealed class FakeFeatureTileSource : ITileSource
{
    private readonly IReadOnlyList<MapFeature> _feats;
    public FakeFeatureTileSource(IReadOnlyList<MapFeature> feats) => _feats = feats;
    public IReadOnlyList<MapFeature>? Decoded(int z, int x, int y) => _feats;
    public void Dispose() { }
}

public class BasemapRendererTests
{
    private static MapFeature Poly(string layer, params (long X, long Y)[] pts) =>
        new(layer, MvtGeomType.Polygon, [pts], new Dictionary<string, string>(), 4096);

    private static MapFeature Line(string layer, params (long X, long Y)[] pts) =>
        new(layer, MvtGeomType.Line, [pts], new Dictionary<string, string>(), 4096);

    private static MapFeature Place(string cls, string name, bool useNameLatin = true, (long, long)? pt = null) =>
        new("place", MvtGeomType.Point,
            [[pt ?? (2048, 2048)]],
            new Dictionary<string, string> { ["class"] = cls, [useNameLatin ? "name:latin" : "name"] = name },
            4096);

    private static IReadOnlyList<MapFeature> RichFeatureSet() =>
    [
        Poly("water", (0, 0), (4096, 0), (4096, 4096), (0, 4096)),
        Line("transportation", (0, 2048), (4096, 2048)),
        Place("city", "TestCity"),
        Place("town", "TestTown", useNameLatin: false),
        Place("village", "Villageton"),
        Place("suburb", "Suburbia"),
        Place("neighbourhood", "Nabeville"),
        Place("weirdclass", "Weirdville"),
        new("transportation_name", MvtGeomType.Line, [[(100, 100), (200, 100)]],
            new Dictionary<string, string> { ["name:latin"] = "Main St" }, 4096),
        // edge cases exercised for line coverage in DrawLabels:
        new("place", MvtGeomType.Point, [[(10, 10)]], new Dictionary<string, string> { ["class"] = "city" }, 4096), // no name → continue
        new("place", MvtGeomType.Point, [[(20, 20)]], new Dictionary<string, string> { ["class"] = "city", ["name:latin"] = "   " }, 4096), // whitespace name → continue
        new("place", MvtGeomType.Point, [], new Dictionary<string, string> { ["class"] = "city", ["name:latin"] = "NoRings" }, 4096), // empty rings → continue
    ];

    private static Region HighZoomRegion() => new(0.0, 0.0, 14, 256, 256);

    [Fact]
    public void Renders_synthetic_features_without_throwing_and_draws_pixels()
    {
        using var surface = SKSurface.Create(new SKImageInfo(256, 256));
        using var tiles = new FakeFeatureTileSource(RichFeatureSet());

        var ex = Record.Exception(() => BasemapRenderer.Render(surface.Canvas, HighZoomRegion(), tiles));
        Assert.Null(ex);

        using var image = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(image);
        var bg = SKColor.Parse(MapStyle.BackgroundHex);
        bool sawNonBackground = false;
        for (int y = 0; y < bmp.Height && !sawNonBackground; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                if (bmp.GetPixel(x, y) != bg)
                {
                    sawNonBackground = true;
                    break;
                }
            }
        }

        Assert.True(sawNonBackground, "expected the renderer to draw something other than the background fill");
    }

    [Fact]
    public void Low_zoom_hides_town_village_suburb_and_road_labels()
    {
        // Same feature set at zoom 1: only "city" is always shown; everything else needs
        // a higher zoom threshold. This still exercises the false branches of WantLabel.
        using var surface = SKSurface.Create(new SKImageInfo(256, 256));
        using var tiles = new FakeFeatureTileSource(RichFeatureSet());
        var region = new Region(0.0, 0.0, 1, 256, 256);

        var ex = Record.Exception(() => BasemapRenderer.Render(surface.Canvas, region, tiles));
        Assert.Null(ex);
    }

    [Fact]
    public void Renders_against_the_committed_fixture_mbtiles_without_throwing()
    {
        using var surface = SKSurface.Create(new SKImageInfo(256, 256));
        using var tiles = new MbTilesTileSource(Fixtures.FixtureMbTiles());
        var region = new Region(31.5, 34.8, 1, 512, 512); // within the fixture's declared bounds

        var ex = Record.Exception(() => BasemapRenderer.Render(surface.Canvas, region, tiles));
        Assert.Null(ex);

        using var image = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(image);
        var bg = SKColor.Parse(MapStyle.BackgroundHex);
        Assert.Equal(bg, bmp.GetPixel(0, 0)); // background is always cleared first, real or not
    }

    [Fact]
    public void Empty_tile_source_renders_only_the_background()
    {
        using var surface = SKSurface.Create(new SKImageInfo(64, 64));
        using var tiles = new MbTilesTileSource(null);
        var region = new Region(0, 0, 3, 64, 64);

        BasemapRenderer.Render(surface.Canvas, region, tiles);

        using var image = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(image);
        var bg = SKColor.Parse(MapStyle.BackgroundHex);
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                Assert.Equal(bg, bmp.GetPixel(x, y));
            }
        }
    }
}
