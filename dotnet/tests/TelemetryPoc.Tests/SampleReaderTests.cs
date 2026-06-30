using TelemetryPoc.Infrastructure;

namespace TelemetryPoc.Tests;

public class SampleReaderTests
{
    [Fact]
    public void LoadSamples_returns_rows_in_ts_order_with_one_value_per_channel()
    {
        using var conn = Fixtures.Open();
        var chans = SqliteRideSource.LoadChannels(conn);
        var samples = SqliteRideSource.LoadSamples(conn, chans);

        Assert.Equal(100, samples.Count);
        Assert.Equal(0, samples[0].TsMs);
        Assert.Equal(100, samples[1].TsMs);
        Assert.All(samples, s => Assert.Equal(chans.Count, s.Values.Length));
        for (int i = 1; i < samples.Count; i++)
            Assert.True(samples[i].TsMs > samples[i - 1].TsMs);
    }
}
