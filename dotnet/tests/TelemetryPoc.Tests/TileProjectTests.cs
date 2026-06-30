using TelemetryPoc.Domain;

namespace TelemetryPoc.Tests;

public class TileProjectTests
{
	[Fact]
	public void ToScreen_maps_tile_local_to_pixels()
	{
		// tile top-left at (100,200); a point at half-x, full-y of a 4096 extent
		var (x, y) = TileProject.ToScreen(100, 200, 2048, 4096, 4096);
		Assert.Equal(100 + 0.5 * 256, x, 6); // 228
		Assert.Equal(200 + 1.0 * 256, y, 6); // 456
	}

	[Fact]
	public void ToScreen_origin_is_the_tile_corner()
	{
		var (x, y) = TileProject.ToScreen(50, 60, 0, 0, 4096);
		Assert.Equal(50.0, x, 6);
		Assert.Equal(60.0, y, 6);
	}

	[Fact]
	public void ToScreen_zero_extent_returns_tile_origin()
	{
		var (x, y) = TileProject.ToScreen(70, 80, 123, 456, 0);
		Assert.Equal(70.0, x, 6);
		Assert.Equal(80.0, y, 6);
	}
}