using TelemetryPoc.Application;
using TelemetryPoc.Domain;

namespace TelemetryPoc.Tests;

public class RideEngineTests
{
    private sealed class FakeMetrics : IMetricsSampler
    {
        public Metrics Sample() => new(0, 0);
    }

    private static RideData Ride() =>
        new(
            new ChannelMeta[]
            {
                new(1, "A", "a", "u", "f", 0, 100, "strip", 0, "I_01"),
                new(2, "B", "b", "u", "f", 0, 100, "gauge", 1, "I_02"),
            },
            [],
            DurationMs: 1000,
            GpsBounds: (1, 2, 3, 4));

    // 6 samples at 0,100,...,500 ms — fed through a fake cursor.
    private static FakeSampleCursor Cursor()
    {
        var samples = new List<Sample>();
        for (int i = 0; i <= 5; i++) samples.Add(new Sample(i * 100, new double[] { i, i * 2 }));
        return new FakeSampleCursor(samples);
    }

    private static RideEngine NewEngine() => new(Ride(), Cursor(), new TelemetryStore(), new FakeMetrics());

    [Fact]
    public void Ctor_applies_meta_to_the_store()
    {
        var e = NewEngine();
        Assert.Equal(2, e.Store.Channels.Count);
        Assert.Equal(1000, e.DurationMs);
        Assert.Equal((1.0, 2.0, 3.0, 4.0), e.GpsBounds);
    }

    [Fact]
    public void Advance_applies_due_samples_and_signals_repaint()
    {
        var e = NewEngine();
        var repaint = e.Advance(wallDeltaMs: 100, nowUnixMs: 1000);
        Assert.True(repaint);
        Assert.Equal(100, e.RideMs);
        Assert.Equal(1000, e.Store.LastEmitUnixMs); // a frame was emitted
    }

    [Fact]
    public void Advance_between_frames_is_an_idle_tick()
    {
        var e = NewEngine();
        e.Advance(100, 1000);          // emits ts 0 and 100, samples the first metrics second
        var repaint = e.Advance(50, 1001); // clock 150: next sample is ts 200, still in second 0
        Assert.False(repaint);
        Assert.Equal(150, e.RideMs);
    }

    [Fact]
    public void Advance_scales_the_wall_delta_by_speed()
    {
        var e = NewEngine();
        e.Advance(wallDeltaMs: 100, nowUnixMs: 1000, speed: 2.0);
        Assert.Equal(200, e.RideMs);
    }

    [Fact]
    public void Paused_freezes_the_clock_and_signals_no_repaint()
    {
        var e = NewEngine();
        e.Advance(100, 1000);
        e.Pause();
        var repaint = e.Advance(100, 2000);
        Assert.False(repaint);
        Assert.Equal(100, e.RideMs); // frozen
        Assert.True(e.IsPaused);

        e.Resume();
        e.Advance(100, 3000);
        Assert.Equal(200, e.RideMs);
        Assert.False(e.IsPaused);
    }

    [Fact]
    public void Seek_jumps_the_clock_remetas_and_raises_reset()
    {
        var e = NewEngine();
        var reset = 0;
        e.Reset += () => reset++;

        e.Seek(0.5, nowUnixMs: 5000); // target 500 ms → lands on the ts-500 sample
        Assert.Equal(1, reset);
        Assert.Equal(500, e.RideMs);
        Assert.Equal(5000, e.Store.LastEmitUnixMs);
    }
}
