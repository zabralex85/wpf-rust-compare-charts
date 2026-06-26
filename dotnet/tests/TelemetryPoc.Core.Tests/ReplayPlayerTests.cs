// dotnet/tests/TelemetryPoc.Core.Tests/ReplayPlayerTests.cs
using Xunit;

namespace TelemetryPoc.Core.Tests;

public class ReplayPlayerTests
{
    private static (IReadOnlyList<ChannelMeta>, IReadOnlyList<EnumValue>, List<Sample>) Setup()
    {
        var channels = new List<ChannelMeta>
        {
            new(1, "Roll", "roll", "deg", "real", -180, 180, "strip", 1, "I_01"),
        };
        var samples = new List<Sample>
        {
            new(0, new double[] { 1 }),
            new(100, new double[] { 2 }),
            new(200, new double[] { 3 }),
        };
        return (channels, new List<EnumValue>(), samples);
    }

    [Fact]
    public void Advance_applies_only_due_samples_at_realtime()
    {
        var (ch, ev, samples) = Setup();
        var store = new TelemetryStore();
        store.ApplyMeta(ch, ev);
        var player = new ReplayPlayer(samples, store, 1.0);

        Assert.Equal(1, player.Advance(0, 1000));     // only ts=0 due
        Assert.Equal(1.0, store.Latest(1));
        Assert.Equal(2, player.Advance(250, 1000));    // ts=100,200 now due
        Assert.Equal(3.0, store.Latest(1));
        Assert.True(player.Done);
        Assert.Equal(0, player.Advance(999, 1000));    // nothing left
    }

    [Fact]
    public void Advance_fast_forward_applies_more_per_elapsed()
    {
        var (ch, ev, samples) = Setup();
        var store = new TelemetryStore();
        store.ApplyMeta(ch, ev);
        var player = new ReplayPlayer(samples, store, 10.0); // 10x

        Assert.Equal(3, player.Advance(20, 5)); // due offsets 0,10,20 all <= 20
        Assert.Equal(5, store.LastEmitUnixMs);
    }
}
