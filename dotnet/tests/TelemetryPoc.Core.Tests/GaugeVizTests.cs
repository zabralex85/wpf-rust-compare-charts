using TelemetryPoc.App.Viz;

namespace TelemetryPoc.Core.Tests;

public class GaugeVizTests
{
	[Fact]
	public void Zero_centers_needle()
	{
		var g = GaugeViz.Compute(0);
		Assert.Equal(-135 + 0.5 * 270, g.AngleDeg, 6); // 0° at center
	}

	[Fact]
	public void Autoscale_picks_nice_round_R()
	{
		// value 20 → raw=26 → ex=1, ff=2.6 → nf=5 → R=50
		var g = GaugeViz.Compute(20);
		Assert.Equal("50", g.Max);
		Assert.Equal("-50", g.Min);
		Assert.Equal("25", g.Q3);
		Assert.Equal("-25", g.Q1);
	}

	[Fact]
	public void Value_at_R_is_full_right()
	{
		// value 20, R=50 → frac=(20+50)/100=0.7 → angle=-135+0.7*270=54
		var g = GaugeViz.Compute(20);
		Assert.Equal(54, g.AngleDeg, 6);
		Assert.Equal("20.000", g.ValueText);
	}

	[Fact]
	public void Clamps_below_min()
	{
		// value -100, raw=130, ex=2, ff=1.3→nf=2→R=200; frac=(-100+200)/400=0.25→angle=-67.5
		var g = GaugeViz.Compute(-100);
		Assert.Equal(-67.5, g.AngleDeg, 6);
	}
}