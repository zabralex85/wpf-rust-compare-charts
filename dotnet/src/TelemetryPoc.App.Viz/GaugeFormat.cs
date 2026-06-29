using System.Globalization;

namespace TelemetryPoc.App.Viz;

public static class GaugeFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Num(double v)
    {
        if (!double.IsFinite(v)) return "—";
        var a = Math.Abs(v);
        return a >= 100 ? v.ToString("F1", Inv) : a >= 1 ? v.ToString("F3", Inv) : v.ToString("F6", Inv);
    }

    public static string Scale(double v)
    {
        if (v == 0) return "0";
        var a = Math.Abs(v);
        var s = a >= 100 ? v.ToString("F0", Inv)
            : a >= 1 ? v.ToString("F1", Inv)
            : a >= 0.1 ? v.ToString("F2", Inv)
            : v.ToString("F3", Inv);
        if (s.Contains('.')) s = s.TrimEnd('0').TrimEnd('.');
        return s;
    }

    public static string GaugeValue(double v)
    {
        if (!double.IsFinite(v)) return "—";
        return Math.Abs(v) >= 100 ? v.ToString("F1", Inv) : v.ToString("F3", Inv);
    }
}
