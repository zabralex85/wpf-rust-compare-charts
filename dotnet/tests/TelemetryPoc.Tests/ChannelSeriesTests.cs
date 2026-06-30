using TelemetryPoc.Domain;

namespace TelemetryPoc.Tests;

public class ChannelSeriesTests
{
    [Fact]
    public void Keeps_window_and_evicts_older_samples()
    {
        var s = new ChannelSeries(1000);
        s.Push(0, 10);
        s.Push(500, 11);
        s.Push(1000, 12);
        s.Push(1600, 13); // window [600,1600] -> drops ts 0 and 500
        var (xs, ys) = s.Arrays();
        Assert.Equal(new long[] { 1000, 1600 }, xs);
        Assert.Equal(new double[] { 12, 13 }, ys);
    }

    [Fact]
    public void Arrays_are_parallel_and_len_tracks_count()
    {
        var s = new ChannelSeries(10_000);
        s.Push(0, 1);
        s.Push(100, 2);
        var (xs, ys) = s.Arrays();
        Assert.Equal(xs.Count, ys.Count);
        Assert.Equal(2, s.Len);
    }
}
