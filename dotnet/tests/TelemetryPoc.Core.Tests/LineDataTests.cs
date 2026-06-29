using TelemetryPoc.App.Viz;
using Xunit;

public class LineDataTests
{
    [Fact]
    public void ToSeconds_divides_by_1000()
        => Assert.Equal(new[] { 0.0, 1.0, 2.0 }, LineData.ToSeconds(new long[] { 0, 1000, 2000 }));

    [Fact]
    public void ToSeconds_empty() => Assert.Empty(LineData.ToSeconds(System.Array.Empty<long>()));

    [Fact]
    public void ScrollWindow_empty_is_zero_to_window_seconds()
        => Assert.Equal((0.0, 60.0), LineData.ScrollWindow(System.Array.Empty<long>(), 60_000));

    [Fact]
    public void ScrollWindow_scrolls_once_span_exceeds_window()
    {
        // last=10000, window=4000 → min=max(0, 6000)=6000 → (6,10)
        Assert.Equal((6.0, 10.0), LineData.ScrollWindow(new long[] { 0, 5000, 10000 }, 4000));
    }

    [Fact]
    public void ScrollWindow_anchors_left_while_shorter_than_window()
    {
        // 5.3s of data in a 60s window → (0, 5.3), not pinned right
        Assert.Equal((0.0, 5.3), LineData.ScrollWindow(new long[] { 0, 1000, 5300 }, 60_000));
    }
}
