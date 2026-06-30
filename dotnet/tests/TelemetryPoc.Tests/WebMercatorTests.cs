using TelemetryPoc.Domain;

namespace TelemetryPoc.Tests;

public class WebMercatorTests
{
	[Fact]
	public void MapSize_is_256_times_2_pow_z()
	{
		Assert.Equal(256.0, WebMercator.MapSize(0));
		Assert.Equal(512.0, WebMercator.MapSize(1));
		Assert.Equal(256.0 * 4096, WebMercator.MapSize(12));
	}

	[Fact]
	public void LonLat_origin_is_map_center()
	{
		var (x, y) = WebMercator.LonLatToWorld(0, 0, 0);
		Assert.Equal(128.0, x, 6);
		Assert.Equal(128.0, y, 6);
	}

	[Theory]
	[InlineData(-180.0, 0.0)]   // left edge
	[InlineData(180.0, 256.0)]  // right edge
	public void LonLat_x_spans_the_world(double lon, double expectedX)
	{
		var (x, _) = WebMercator.LonLatToWorld(lon, 0, 0);
		Assert.Equal(expectedX, x, 6);
	}

	[Fact]
	public void LonLat_north_is_smaller_y()
	{
		// max-mercator latitude → y ≈ 0 (top); south → y ≈ 256 (bottom)
		var (_, yNorth) = WebMercator.LonLatToWorld(0, 85.05112878, 0);
		var (_, ySouth) = WebMercator.LonLatToWorld(0, -85.05112878, 0);
		Assert.Equal(0.0, yNorth, 3);
		Assert.Equal(256.0, ySouth, 3);
	}

	[Fact]
	public void TileXY_floors_world_over_256()
	{
		// z1: MapSize 512, origin world (256,256) → tile (1,1)
		Assert.Equal((1, 1), WebMercator.TileXY(0, 0, 1));
		// top-left corner
		Assert.Equal((0, 0), WebMercator.TileXY(-180, 85.05112878, 1));
	}

	[Theory]
	[InlineData(34.78, 32.08, 12)]
	[InlineData(-0.13, 51.50, 9)]
	[InlineData(139.69, 35.69, 14)]
	public void WorldToLonLat_round_trips_LonLatToWorld(double lon, double lat, int z)
	{
		var (wx, wy) = WebMercator.LonLatToWorld(lon, lat, z);
		var (lon2, lat2) = WebMercator.WorldToLonLat(wx, wy, z);
		Assert.Equal(lon, lon2, 4);
		Assert.Equal(lat, lat2, 4);
	}
}