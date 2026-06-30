using TelemetryPoc.Application;

namespace TelemetryPoc.Tests;

public class PacerTests
{
    [Fact]
    public void Realtime_due_offset_equals_ts()
    {
        var p = new Pacer(1.0);
        Assert.Equal(0, p.DueOffsetMs(0));
        Assert.Equal(100, p.DueOffsetMs(100));
        Assert.Equal(43_200_000, p.DueOffsetMs(43_200_000));
    }

    [Fact]
    public void Fast_forward_compresses_time()
    {
        Assert.Equal(100, new Pacer(10.0).DueOffsetMs(1000));
    }

    [Fact]
    public void Wait_never_negative_and_accounts_for_elapsed()
    {
        var p = new Pacer(1.0);
        Assert.Equal(100, p.WaitMs(100, 0));
        Assert.Equal(60, p.WaitMs(100, 40));
        Assert.Equal(0, p.WaitMs(100, 500));
    }

    [Fact]
    public void Nonpositive_speed_clamps_to_realtime()
    {
        Assert.Equal(100, new Pacer(0).DueOffsetMs(100));
        Assert.Equal(100, new Pacer(-5).DueOffsetMs(100));
    }
}
