using System.Globalization;

namespace TelemetryPoc.App.Viz;

public static class MissionClock
{
    public static string Format(long ms)
    {
        if (ms < 0) ms = 0;
        var t = TimeSpan.FromMilliseconds(ms);
        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}.{3:000}",
            (int)t.TotalHours, t.Minutes, t.Seconds, t.Milliseconds);
    }

    public static string FormatTPlus(long ms) => "T+" + Format(ms);
}
