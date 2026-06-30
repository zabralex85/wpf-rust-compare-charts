using TelemetryPoc.Presentation;

namespace TelemetryPoc.Tests;

public class NearestSampleTests
{
	[Fact]
	public void Empty_returns_minus_one()
		=> Assert.Equal(-1, NearestSample.IndexOf([], 5));

	[Theory]
	[InlineData(2.4, 2)]   // closest to xs[2]=2
	[InlineData(2.6, 3)]   // closest to xs[3]=3
	[InlineData(-5, 0)]    // below range → first
	[InlineData(99, 5)]    // above range → last
	public void Finds_closest_index(double target, int expected)
	{
		var xs = new double[] { 0, 1, 2, 3, 4, 5 };
		Assert.Equal(expected, NearestSample.IndexOf(xs, target));
	}
}