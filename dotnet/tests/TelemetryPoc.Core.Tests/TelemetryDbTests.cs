using Xunit;

namespace TelemetryPoc.Core.Tests;

public class TelemetryDbTests
{
    [Fact]
    public void LoadChannels_returns_thirty_in_display_order()
    {
        using var conn = Fixtures.Open();
        var chans = TelemetryDb.LoadChannels(conn);
        Assert.Equal(30, chans.Count);
        Assert.Equal(1, chans[0].Id);
        var orders = chans.Select(c => c.DisplayOrder).ToList();
        Assert.Equal(orders.OrderBy(x => x).ToList(), orders);
    }

    [Fact]
    public void LoadEnumValues_includes_inu_mode2_labels()
    {
        using var conn = Fixtures.Open();
        var evs = TelemetryDb.LoadEnumValues(conn);
        var labels = evs.Select(e => e.Label).ToList();
        Assert.Contains("Normal", labels);
        Assert.Contains("Critical", labels);
    }

    [Fact]
    public void LoadRideMeta_reports_rate_and_channel_count()
    {
        using var conn = Fixtures.Open();
        var meta = TelemetryDb.LoadRideMeta(conn);
        Assert.Equal(10, meta.RateHz);
        Assert.Equal(30, meta.ChannelCount);
    }
}
