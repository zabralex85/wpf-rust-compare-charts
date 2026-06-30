using System.Collections.Generic;
using TelemetryPoc.Infrastructure;
using Xunit;

namespace TelemetryPoc.Tests;

public class SqliteSampleCursorTests
{
    private static List<string> Cols()
    {
        using var conn = Fixtures.Open();
        var cols = new List<string>();
        foreach (var c in SqliteRideSource.LoadChannels(conn))
        {
            cols.Add(c.ColumnName);
        }

        return cols;
    }

    [Fact]
    public void Streams_rows_in_ts_order_then_ends()
    {
        using var cur = new SqliteSampleCursor(Fixtures.RideSmallDb(), Cols());
        Assert.Equal(0, cur.PeekTs);
        long last = -1; int n = 0;
        while (cur.PeekTs is { } t)
        {
            Assert.True(t > last);
            var s = cur.Read();
            Assert.Equal(t, s.TsMs);
            last = t; n++;
        }

        Assert.Equal(100, n);          // ride_small.db has 100 samples
        Assert.Null(cur.PeekTs);
    }

    [Fact]
    public void SeekTo_lands_on_first_row_at_or_after_target()
    {
        using var cur = new SqliteSampleCursor(Fixtures.RideSmallDb(), Cols());
        cur.SeekTo(150);               // rows at 0,100,200,...; first >= 150 is 200
        Assert.Equal(200, cur.PeekTs);
    }

    [Fact]
    public void SeekTo_past_end_is_null()
    {
        using var cur = new SqliteSampleCursor(Fixtures.RideSmallDb(), Cols());
        cur.SeekTo(1_000_000);
        Assert.Null(cur.PeekTs);
    }
}
