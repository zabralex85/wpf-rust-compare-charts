namespace TelemetryPoc.Presentation;

public static class LineData
{
    public static double[] ToSeconds(IReadOnlyList<long> xsMs)
    {
        var a = new double[xsMs.Count];
        for (int i = 0; i < a.Length; i++) a[i] = xsMs[i] / 1000.0;
        return a;
    }

    public static (double Min, double Max) ScrollWindow(IReadOnlyList<long> xsMs, long windowMs)
    {
        var w = Math.Max(1, windowMs);
        if (xsMs.Count == 0) return (0, w / 1000.0);
        var last = xsMs[^1];
        var first = xsMs[0];
        var min = Math.Max(first, last - w);
        return (min / 1000.0, last / 1000.0);
    }
}
