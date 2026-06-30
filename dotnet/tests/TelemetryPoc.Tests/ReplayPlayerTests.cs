// dotnet/tests/TelemetryPoc.Tests/ReplayPlayerTests.cs
using TelemetryPoc.Application;
using TelemetryPoc.Domain;

namespace TelemetryPoc.Tests;

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

    private static (ReplayPlayer, TelemetryStore) NewPlayer()
    {
        var (ch, ev, samples) = Setup();
        var store = new TelemetryStore();
        store.ApplyMeta(ch, ev);
        return (new ReplayPlayer(new FakeSampleCursor(samples), store), store);
    }

    [Fact]
    public void Advance_applies_only_samples_at_or_before_rideMs()
    {
        var (player, store) = NewPlayer();
        Assert.Equal(1, player.Advance(0, 1000));      // only ts=0 due
        Assert.Equal(1.0, store.Latest(1));
        Assert.Equal(2, player.Advance(250, 1000));    // ts=100,200 now due
        Assert.Equal(3.0, store.Latest(1));
        Assert.True(player.Done);
        Assert.Equal(0, player.Advance(999, 1000));    // nothing left
    }

    [Fact]
    public void Advance_applies_all_when_rideMs_past_last()
    {
        var (player, store) = NewPlayer();
        Assert.Equal(3, player.Advance(200, 5));       // ts 0,100,200 all <= 200
        Assert.Equal(5, store.LastEmitUnixMs);
    }

    [Fact]
    public void SeekTo_lands_on_first_sample_at_or_after_target()
    {
        var (player, _) = NewPlayer();
        player.SeekTo(50);                 // first ts >= 50 is ts=100
        Assert.Equal(100, player.PeekTs);
    }

    [Fact]
    public void SeekTo_before_first_is_index_zero()
    {
        var (player, _) = NewPlayer();
        player.SeekTo(-10);
        Assert.Equal(0, player.PeekTs);
    }

    [Fact]
    public void SeekTo_past_last_is_count_and_done()
    {
        var (player, _) = NewPlayer();
        player.SeekTo(999);
        Assert.True(player.Done);
        Assert.Null(player.PeekTs);
    }

    [Fact]
    public void Advance_after_SeekTo_applies_from_new_index()
    {
        var (player, store) = NewPlayer();
        player.SeekTo(200);                            // jump to ts=200
        Assert.Equal(1, player.Advance(200, 7));       // applies only ts=200
        Assert.Equal(3.0, store.Latest(1));
        Assert.True(player.Done);
    }
}
