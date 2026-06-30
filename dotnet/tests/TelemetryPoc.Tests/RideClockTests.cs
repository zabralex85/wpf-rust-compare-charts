using TelemetryPoc.Domain;

namespace TelemetryPoc.Tests;

public class RideClockTests
{
    [Fact]
    public void Advance_accumulates_when_playing()
    {
        var c = new RideClock();
        c.Advance(100);
        c.Advance(50);
        Assert.Equal(150, c.RideMs);
        Assert.True(c.Playing);
    }

    [Fact]
    public void Advance_is_noop_when_paused()
    {
        var c = new RideClock();
        c.Advance(100);
        c.Pause();
        c.Advance(100);
        Assert.Equal(100, c.RideMs);
        Assert.False(c.Playing);
    }

    [Fact]
    public void Resume_lets_clock_advance_again()
    {
        var c = new RideClock();
        c.Pause();
        c.Advance(100);
        c.Resume();
        c.Advance(40);
        Assert.Equal(40, c.RideMs);
    }

    [Fact]
    public void Advance_ignores_zero_and_negative_delta()
    {
        var c = new RideClock();
        c.Advance(0);
        c.Advance(-25);
        Assert.Equal(0, c.RideMs);
    }

    [Fact]
    public void SeekTo_clamps_low_high_and_midrange()
    {
        var c = new RideClock();
        Assert.Equal(0, c.SeekTo(-500, 1000));      // below 0
        Assert.Equal(1000, c.SeekTo(9999, 1000));   // above duration
        Assert.Equal(400, c.SeekTo(400, 1000));     // mid-range
        Assert.Equal(400, c.RideMs);
    }

    [Fact]
    public void SeekTo_does_not_change_playing()
    {
        var c = new RideClock();
        c.Pause();
        c.SeekTo(300, 1000);
        Assert.False(c.Playing);
        Assert.Equal(300, c.RideMs);
    }
}
