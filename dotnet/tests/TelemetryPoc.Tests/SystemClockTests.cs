using TelemetryPoc.Infrastructure;

namespace TelemetryPoc.Tests;

public class SystemClockTests
{
    [Fact]
    public void UtcNowUnixMs_returns_a_sane_recent_timestamp()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var clock = new SystemClock();
        var now = clock.UtcNowUnixMs;
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Assert.InRange(now, before, after);
    }
}
