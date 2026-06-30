using TelemetryPoc.Presentation;

namespace TelemetryPoc.Tests;

public class LineAxisTests
{
	[Theory]
	[InlineData(0.0, "0:00")]
	[InlineData(5.0, "0:05")]
	[InlineData(70.0, "1:10")]
	[InlineData(600.0, "10:00")]
	[InlineData(12.9, "0:12")]
	[InlineData(-3.0, "0:00")]
	public void FormatElapsed_is_m_ss(double sec, string s) => Assert.Equal(s, LineAxis.FormatElapsed(sec));
}