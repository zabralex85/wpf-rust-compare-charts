using TelemetryPoc.Domain;

namespace TelemetryPoc.Tests;

public class MapInteractTests
{
	private static Region R() => new(32.08, 34.78, 12, 400, 300);

	[Fact]
	public void Pan_right_moves_center_west()
	{
		var r = R();
		var p = MapInteract.Pan(r, 50, 0); // drag content right by 50px
		Assert.True(p.CenterLon < r.CenterLon); // viewport shifted, center longitude decreases
		Assert.Equal(r.CenterLat, p.CenterLat, 6);
		Assert.Equal(r.Zoom, p.Zoom);
	}

	[Fact]
	public void Pan_down_moves_center_north()
	{
		var p = MapInteract.Pan(R(), 0, 40);
		Assert.True(p.CenterLat > R().CenterLat); // dragging down reveals north
	}

	[Fact]
	public void ZoomAt_increments_zoom_and_clamps()
	{
		var r = R();
		var z13 = MapInteract.ZoomAt(r, 200, 150, +1, 9, 14);
		Assert.Equal(13, z13.Zoom);
		var capped = MapInteract.ZoomAt(r with { Zoom = 14 }, 200, 150, +1, 9, 14);
		Assert.Equal(14, capped.Zoom); // no change at max
		var floored = MapInteract.ZoomAt(r with { Zoom = 9 }, 200, 150, -1, 9, 14);
		Assert.Equal(9, floored.Zoom);
	}

	[Fact]
	public void ZoomAt_keeps_cursor_geopoint_under_cursor()
	{
		var r = R();
		double cx = 120, cy = 90;
		var (curLon, curLat) = WebMercator.WorldToLonLat(
			WebMercator.LonLatToWorld(r.CenterLon, r.CenterLat, r.Zoom).X - r.Width / 2 + cx,
			WebMercator.LonLatToWorld(r.CenterLon, r.CenterLat, r.Zoom).Y - r.Height / 2 + cy,
			r.Zoom);
		var z = MapInteract.ZoomAt(r, cx, cy, +1, 9, 14);
		var (sx, sy) = MapProject.GpsToScreen(z, curLat, curLon);
		Assert.Equal(cx, sx, 1); // same screen pixel after zoom
		Assert.Equal(cy, sy, 1);
	}
}