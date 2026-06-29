namespace TelemetryPoc.Map;

public sealed record LabelBox(string Text, double X, double Y, double W, double H);

public static class LabelLayout
{
    public static IReadOnlyList<LabelBox> Place(IReadOnlyList<LabelBox> candidates)
    {
        var placed = new List<LabelBox>();
        foreach (var c in candidates)
        {
            bool overlaps = false;
            foreach (var p in placed)
            {
                if (c.X < p.X + p.W && c.X + c.W > p.X && c.Y < p.Y + p.H && c.Y + c.H > p.Y)
                { overlaps = true; break; }
            }
            if (!overlaps) placed.Add(c);
        }
        return placed;
    }
}
