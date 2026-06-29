using System.Collections.Generic;
using System.Linq;
using TelemetryPoc.App.Viz;
using TelemetryPoc.Core;
using Xunit;

public class WidgetSeedTests
{
    private static ChannelMeta Ch(int id, string widget, string name = "n", string unit = "u")
        => new ChannelMeta(id, name, $"col{id}", unit, "f64", 0, 100, widget, id, "0x0");

    [Fact]
    public void Seeds_map_gauges_and_lines_with_expected_sizes()
    {
        var channels = new List<ChannelMeta>
        {
            Ch(1, "map_lat"), Ch(2, "map_lon"),
            Ch(3, "gauge"), Ch(4, "gauge"),
            Ch(5, "strip"),
            Ch(6, "table"), // ignored
        };
        var w = WidgetSeed.SeedLayout(channels);

        var map = Assert.Single(w.Where(x => x.Kind == WidgetKind.Map));
        Assert.Equal((4, 4), (map.Cols, map.Rows));
        Assert.Equal("map", map.Id);

        Assert.Equal(2, w.Count(x => x.Kind == WidgetKind.Gauge));
        Assert.All(w.Where(x => x.Kind == WidgetKind.Gauge), g => Assert.Equal((1, 1), (g.Cols, g.Rows)));
        Assert.Contains(w, x => x.Id == "gauge-3");

        var line = Assert.Single(w.Where(x => x.Kind == WidgetKind.Line));
        Assert.Equal((2, 1), (line.Cols, line.Rows));
        Assert.Equal("line-5", line.Id);
        Assert.Equal(5, line.ChannelId);
    }

    [Fact]
    public void No_map_when_lat_or_lon_missing()
    {
        var w = WidgetSeed.SeedLayout(new List<ChannelMeta> { Ch(1, "map_lat"), Ch(2, "gauge") });
        Assert.DoesNotContain(w, x => x.Kind == WidgetKind.Map);
    }

    [Fact]
    public void FirstFit_packs_row_major_without_overlap()
    {
        var placed = new List<Widget>();
        var a = WidgetLayout.FirstFit(placed, 4, 4);     // (1,1)
        placed.Add(new Widget("a", WidgetKind.Map, null, "", "", a.Col, a.Row, 4, 4, 1));
        var b = WidgetLayout.FirstFit(placed, 1, 1);     // first free after the 4x4 block
        Assert.Equal((1, 1), a);
        Assert.Equal((5, 1), b); // cols 1-4 taken on row 1, next free col is 5
    }
}
