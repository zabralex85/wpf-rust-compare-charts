using System.Collections.Generic;
using System.Linq;
using TelemetryPoc.Map;
using Xunit;

public class LabelLayoutTests
{
    [Fact]
    public void Place_drops_overlapping_candidates()
    {
        var cands = new List<LabelBox>
        {
            new("A", 0, 0, 50, 10),
            new("B", 10, 0, 50, 10),   // overlaps A → dropped
            new("C", 100, 100, 50, 10) // clear → kept
        };
        var placed = LabelLayout.Place(cands);
        Assert.Equal(new[] { "A", "C" }, placed.Select(p => p.Text).ToArray());
    }
}
