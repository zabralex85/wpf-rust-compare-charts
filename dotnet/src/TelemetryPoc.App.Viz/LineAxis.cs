using System.Globalization;

namespace TelemetryPoc.App.Viz;

public static class LineAxis
{
    public static string FormatElapsed(double sec)
    {
        var s = (long)Math.Max(0, Math.Floor(sec));
        var m = s / 60;
        var r = s % 60;
        return m + ":" + r.ToString("00", CultureInfo.InvariantCulture);
    }
}
