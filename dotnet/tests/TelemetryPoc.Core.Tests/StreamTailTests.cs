using TelemetryPoc.App.Viz;

namespace TelemetryPoc.Core.Tests;

public class StreamTailTests
{
	[Fact]
	public void Empty_returns_zero()
		=> Assert.Equal(0, StreamTail.NewFrom([], 5));

	[Fact]
	public void All_new_when_lastX_precedes()
		=> Assert.Equal(0, StreamTail.NewFrom([1.0, 2.0, 3.0], -1));

	[Fact]
	public void Returns_index_of_first_point_past_lastX()
		=> Assert.Equal(2, StreamTail.NewFrom([1.0, 2.0, 3.0, 4.0], 2.0)); // 3.0,4.0 are new

	[Fact]
	public void Nothing_new_returns_length()
		=> Assert.Equal(3, StreamTail.NewFrom([1.0, 2.0, 3.0], 3.0));

	[Fact]
	public void Reset_returns_minus_one_when_time_went_backwards()
		=> Assert.Equal(-1, StreamTail.NewFrom([0.5, 1.0], 100.0)); // last 1.0 < lastX 100 → reset
}