using TelemetryPoc.Map;

namespace TelemetryPoc.Core.Tests;

public class MapStyleOpacityTests
{
	[Theory]
	[InlineData("landcover", 0.8)]
	[InlineData("landuse", 0.6)]
	[InlineData("building", 0.8)]
	[InlineData("water", 1.0)]
	public void Layers_have_fill_opacity(string id, double opacity)
		=> Assert.Equal(opacity, MapStyle.Layers.First(l => l.Id == id).Opacity, 6);
}