using TelemetryPoc.Map;

namespace TelemetryPoc.Core.Tests;

public class LabelLayoutTests
{
	[Fact]
	public void Place_drops_overlapping_candidates()
	{
		List<LabelBox> cands =
		[
			new("A", 0, 0, 50, 10),
			new("B", 10, 0, 50, 10),   // overlaps A → dropped
			new("C", 100, 100, 50, 10) // clear → kept
		];
		var placed = LabelLayout.Place(cands);
		Assert.Equal(["A", "C"], placed.Select(p => p.Text).ToArray());
	}
}