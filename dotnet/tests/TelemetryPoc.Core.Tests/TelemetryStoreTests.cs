namespace TelemetryPoc.Core.Tests;

public class TelemetryStoreTests
{
    private static (IReadOnlyList<ChannelMeta>, IReadOnlyList<EnumValue>) Meta()
    {
        var channels = new List<ChannelMeta>
        {
            new(1, "Roll", "roll", "deg", "real", -180, 180, "strip", 1, "I_01"),
            new(2, "Lat", "lat", "deg", "real", 31, 33, "map_lat", 2, "I_09"),
            new(3, "Lon", "lon", "deg", "real", 34, 35, "map_lon", 3, "I_09"),
        };
        var enums = new List<EnumValue> { new(9, 1, "Critical", "critical") };
        return (channels, enums);
    }

    [Fact]
    public void ApplyFrame_records_latest_and_buffers_only_strip_channels()
    {
        var s = new TelemetryStore();
        var (ch, ev) = Meta();
        s.ApplyMeta(ch, ev);
        s.ApplyFrame(new Sample(100, new[] { 12.5, 32.0, 34.5 }), 1700000000000);
        Assert.Equal(12.5, s.Latest(1));
        Assert.Equal(1, s.Series(1)!.Len);
        Assert.Null(s.Series(2));
        Assert.Equal(1700000000000, s.LastEmitUnixMs);
    }

    [Fact]
    public void Accumulates_gps_track_from_map_channels()
    {
        var s = new TelemetryStore();
        var (ch, ev) = Meta();
        s.ApplyMeta(ch, ev);
        s.ApplyFrame(new Sample(0, new[] { 0, 32.0, 34.5 }), 1);
        s.ApplyFrame(new Sample(100, new[] { 0, 32.1, 34.6 }), 2);
        var (lat, lon) = s.GpsTrack();
        Assert.Equal(new[] { 32.0, 32.1 }, lat);
        Assert.Equal(new[] { 34.5, 34.6 }, lon);
    }

    [Fact]
    public void Ignores_frame_with_mismatched_value_count()
    {
        var s = new TelemetryStore();
        var (ch, ev) = Meta();
        s.ApplyMeta(ch, ev);
        s.ApplyFrame(new Sample(0, new double[] { 1, 2 }), 1); // too short
        Assert.Null(s.Latest(1));
        Assert.Equal(0, s.Series(1)!.Len);
    }

    [Fact]
    public void Re_meta_resets_latest_and_track()
    {
        var s = new TelemetryStore();
        var (ch, ev) = Meta();
        s.ApplyMeta(ch, ev);
        s.ApplyFrame(new Sample(0, new[] { 1, 32, 34.5 }), 5);
        s.ApplyMeta(ch, ev);
        Assert.Null(s.Latest(1));
        Assert.Empty(s.GpsTrack().Lat);
        Assert.Equal(0, s.LastEmitUnixMs);
    }
}
