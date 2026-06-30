using TelemetryPoc.Domain;

namespace TelemetryPoc.Presentation;

/// <summary>Builds the initial widget layout from channel metadata, mirroring the
/// Rust app's layout.ts: a 4×4 map (if lat+lon exist), a 1×1 gauge per gauge
/// channel, a 2×1 line per strip channel — each placed via first-fit.</summary>
public static class WidgetSeed
{
    public static IReadOnlyList<Widget> SeedLayout(IReadOnlyList<ChannelMeta> channels)
    {
        var placed = new List<Widget>();

        bool hasLat = false, hasLon = false;
        foreach (var ch in channels)
        {
            if (ch.Widget == "map_lat")
			{
				hasLat = true;
			}

            if (ch.Widget == "map_lon")
			{
				hasLon = true;
			}
		}

        if (hasLat && hasLon)
        {
            var (c, r) = WidgetLayout.FirstFit(placed, 4, 4);
            placed.Add(new Widget("map", WidgetKind.Map, null, "FLIGHT TRACK", c, r, 4, 4));
        }

        foreach (var ch in channels)
		{
			if (ch.Widget == "gauge")
            {
                var (c, r) = WidgetLayout.FirstFit(placed, 1, 1);
                placed.Add(new Widget($"gauge-{ch.Id}", WidgetKind.Gauge, (int)ch.Id, ch.Name, c, r, 1, 1));
            }
		}

        foreach (var ch in channels)
		{
			if (ch.Widget == "strip")
            {
                var (c, r) = WidgetLayout.FirstFit(placed, 2, 1);
                placed.Add(new Widget($"line-{ch.Id}", WidgetKind.Line, (int)ch.Id, ch.Name, c, r, 2, 1));
            }
		}

        return placed;
    }
}
