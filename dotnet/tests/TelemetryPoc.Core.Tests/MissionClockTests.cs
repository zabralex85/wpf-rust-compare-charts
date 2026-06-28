using TelemetryPoc.App.Viz;
using Xunit;

public class MissionClockTests
{
    [Theory]
    [InlineData(0, "00:00:00.000")]
    [InlineData(5000, "00:00:05.000")]
    [InlineData(65_432, "00:01:05.432")]
    [InlineData(3_661_007, "01:01:01.007")]
    [InlineData(-50, "00:00:00.000")]
    public void Format_is_hms_millis(long ms, string expected)
        => Assert.Equal(expected, MissionClock.Format(ms));

    [Fact]
    public void FormatTPlus_prefixes()
        => Assert.Equal("T+00:00:05.000", MissionClock.FormatTPlus(5000));
}
