using TelemetryPoc.Presentation;

namespace TelemetryPoc.Tests;

public class GaugeFormatTests
{
	[Theory]
	[InlineData(0.0, "0.000000")]
	[InlineData(0.5, "0.500000")]
	[InlineData(1.5, "1.500")]
	[InlineData(250.0, "250.0")]
	public void Num_picks_precision_by_magnitude(double v, string s) => Assert.Equal(s, GaugeFormat.Num(v));

	[Fact]
	public void Num_non_finite_is_dash() => Assert.Equal("—", GaugeFormat.Num(double.NaN));

	[Theory]
	[InlineData(0.0, "0")]
	[InlineData(250.0, "250")]
	[InlineData(2.5, "2.5")]
	[InlineData(1.0, "1")]
	[InlineData(0.25, "0.25")]
	[InlineData(0.05, "0.05")]
	public void Scale_strips_trailing_zeros(double v, string s) => Assert.Equal(s, GaugeFormat.Scale(v));

	[Theory]
	[InlineData(250.0, "250.0")]
	[InlineData(1.5, "1.500")]
	public void GaugeValue_two_band(double v, string s) => Assert.Equal(s, GaugeFormat.GaugeValue(v));
}