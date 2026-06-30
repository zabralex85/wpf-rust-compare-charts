using TelemetryPoc.Map;

namespace TelemetryPoc.Core.Tests;

public class TileMathTests
{
	[Fact]
	public void VisibleTiles_z0_single_tile_at_origin()
	{
		var tiles = TileMath.VisibleTiles(new Region(0, 0, 0, 256, 256));
		Assert.Single(tiles);
		var t = tiles[0];
		Assert.Equal((0, 0, 0), (t.Z, t.X, t.Y));
		Assert.Equal(0.0, t.ScreenX, 6);
		Assert.Equal(0.0, t.ScreenY, 6);
	}

	[Fact]
	public void VisibleTiles_z1_four_tiles_with_offsets()
	{
		var tiles = TileMath.VisibleTiles(new Region(0, 0, 1, 512, 512))
			.OrderBy(t => t.Y).ThenBy(t => t.X).ToList();
		Assert.Equal(4, tiles.Count);
		Assert.Equal((0, 0, 0.0, 0.0), (tiles[0].X, tiles[0].Y, tiles[0].ScreenX, tiles[0].ScreenY));
		Assert.Equal((1, 0, 256.0, 0.0), (tiles[1].X, tiles[1].Y, tiles[1].ScreenX, tiles[1].ScreenY));
		Assert.Equal((0, 1, 0.0, 256.0), (tiles[2].X, tiles[2].Y, tiles[2].ScreenX, tiles[2].ScreenY));
		Assert.Equal((1, 1, 256.0, 256.0), (tiles[3].X, tiles[3].Y, tiles[3].ScreenX, tiles[3].ScreenY));
	}

	[Fact]
	public void FitBbox_center_is_midpoint()
	{
		var (lat, lon, _) = TileMath.FitBbox(32.0, 34.7, 32.1, 34.9, 400, 400);
		Assert.Equal(32.05, lat, 6);
		Assert.Equal(34.8, lon, 6);
	}

	[Fact]
	public void FitBbox_tiny_bbox_picks_max_zoom()
	{
		// ~0.001° span fits at the highest allowed zoom
		var (_, _, z) = TileMath.FitBbox(32.080, 34.780, 32.081, 34.781, 400, 400);
		Assert.Equal(14, z);
	}

	[Fact]
	public void FitBbox_huge_bbox_clamps_to_min_zoom()
	{
		var (_, _, z) = TileMath.FitBbox(-40, -100, 60, 100, 400, 400);
		Assert.Equal(TileMath.MinZoom, z);
	}
}