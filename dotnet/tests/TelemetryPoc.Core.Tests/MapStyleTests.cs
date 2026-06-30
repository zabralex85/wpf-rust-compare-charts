using TelemetryPoc.Map;

namespace TelemetryPoc.Core.Tests;

public class MapStyleTests
{
	[Fact]
	public void Background_is_inu_dark()
		=> Assert.Equal("#0a0e14", MapStyle.BackgroundHex);

	[Fact]
	public void Layers_are_in_draw_order_with_inu_colors()
	{
		var ids = MapStyle.Layers.Select(l => l.Id).ToArray();
		Assert.Equal(
			new[] { "water", "landcover", "landuse", "transportation-casing", "transportation", "building" },
			ids);
	}

	[Fact]
	public void Water_is_a_fill_with_the_inu_color()
	{
		var water = MapStyle.Layers.First(l => l.Id == "water");
		Assert.Equal(PaintKind.Fill, water.Kind);
		Assert.Equal("water", water.SourceLayer);
		Assert.Equal("#16384f", water.ColorHex);
	}

	[Fact]
	public void Transportation_layers_share_source_but_differ()
	{
		var casing = MapStyle.Layers.First(l => l.Id == "transportation-casing");
		var road = MapStyle.Layers.First(l => l.Id == "transportation");
		Assert.Equal("transportation", casing.SourceLayer);
		Assert.Equal("transportation", road.SourceLayer);
		Assert.Equal(PaintKind.Line, casing.Kind);
		Assert.Equal("#0a0e14", casing.ColorHex);
		Assert.Equal("#5b6470", road.ColorHex);
		Assert.True(casing.Width > road.Width); // casing is wider
	}
}