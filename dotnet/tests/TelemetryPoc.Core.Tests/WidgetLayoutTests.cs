using TelemetryPoc.App.Viz;
using Xunit;

public class WidgetLayoutTests
{
    [Theory]
    [InlineData(0, 0, 1, 1)]       // top-left cell is (1,1)
    [InlineData(10, 10, 1, 1)]     // within first pitch
    [InlineData(168, 0, 2, 1)]     // one pitch right → col 2
    [InlineData(0, 336, 1, 3)]     // two pitches down → row 3
    public void CellFromPoint_is_one_indexed_and_pitch_snapped(double x, double y, int col, int row)
    {
        var c = WidgetLayout.CellFromPoint(x, y);
        Assert.Equal((col, row), c);
    }

    [Theory]
    [InlineData(0, 0)]      // no movement
    [InlineData(23, 0)]     // inside deadzone
    [InlineData(24, 1)]     // 24px past start = +1 cell (pitch-deadzone)
    [InlineData(192, 2)]    // 168+24
    [InlineData(-24, -1)]   // shrink one cell
    public void ResizeStep_uses_144_deadzone(double delta, int steps)
        => Assert.Equal(steps, WidgetLayout.ResizeStep(delta));

    [Fact]
    public void ClampSize_gauge_is_square_and_capped()
    {
        Assert.Equal((3, 3), WidgetLayout.ClampSize(WidgetKind.Gauge, 3, 2)); // square = max
        Assert.Equal((6, 6), WidgetLayout.ClampSize(WidgetKind.Gauge, 9, 1)); // cap 6
        Assert.Equal((1, 1), WidgetLayout.ClampSize(WidgetKind.Gauge, 0, 0)); // floor 1
    }

    [Fact]
    public void ClampSize_line_and_map_bounds()
    {
        Assert.Equal((6, 4), WidgetLayout.ClampSize(WidgetKind.Line, 9, 9)); // line 6x4 cap
        Assert.Equal((1, 1), WidgetLayout.ClampSize(WidgetKind.Line, 0, 0));
        Assert.Equal((2, 2), WidgetLayout.ClampSize(WidgetKind.Map, 1, 1)); // map min 2
        Assert.Equal((8, 6), WidgetLayout.ClampSize(WidgetKind.Map, 99, 99)); // map cap
    }

    [Fact]
    public void Toggle_gauge_to_line_then_back()
    {
        var (k1, c1, r1) = WidgetLayout.Toggle(WidgetKind.Gauge, 1, 1);
        Assert.Equal((WidgetKind.Line, 2, 1), (k1, c1, r1)); // min 2 cols
        var (k2, c2, r2) = WidgetLayout.Toggle(WidgetKind.Line, 4, 3);
        Assert.Equal((WidgetKind.Gauge, 1, 1), (k2, c2, r2)); // back to 1x1
    }

    [Theory]
    [InlineData(1, 2.0, 2)]
    [InlineData(4, 2.0, 8)]
    [InlineData(8, 2.0, 8)]   // clamp top
    [InlineData(2, 0.5, 1)]
    [InlineData(1, 0.5, 1)]   // clamp bottom
    public void ZoomBy_clamps_1_to_8(int z, double f, int expected)
        => Assert.Equal(expected, WidgetLayout.ZoomBy(z, f));
}
