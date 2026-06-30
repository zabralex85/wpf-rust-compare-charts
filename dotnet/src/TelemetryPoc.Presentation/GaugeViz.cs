namespace TelemetryPoc.Presentation;

public sealed record GaugeVizResult(double AngleDeg, string Min, string Q1, string Q3, string Max, string ValueText);

public static class GaugeViz
{
    public static GaugeVizResult Compute(double value)
    {
        var raw = Math.Max(Math.Abs(value) * 1.3, 1e-6);
        var ex = Math.Floor(Math.Log10(raw));
        var ff = raw / Math.Pow(10, ex);
        double nf = ff <= 1 ? 1 : ff <= 2 ? 2 : ff <= 2.5 ? 2.5 : ff <= 5 ? 5 : 10;
        var r = nf * Math.Pow(10, ex);

        var frac = Math.Max(0, Math.Min(1, (value + r) / (2 * r)));
        var angleDeg = -135 + frac * 270;

        return new GaugeVizResult(
            angleDeg,
            GaugeFormat.Scale(-r), GaugeFormat.Scale(-r / 2),
            GaugeFormat.Scale(r / 2), GaugeFormat.Scale(r),
            GaugeFormat.Num(value));
    }
}
