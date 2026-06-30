namespace TelemetryPoc.Presentation;

public static class StreamTail
{
    /// <summary>Index of the first x &gt; lastX (the new tail), xs.Length if none new,
    /// or -1 if the series reset (last x &lt; lastX, time went backwards).</summary>
    public static int NewFrom(double[] xs, double lastX)
    {
        if (xs.Length == 0)
        {
            return 0;
        }

        if (xs[^1] < lastX)
        {
            return -1; // re-meta reset
        }

        int i = 0;
        while (i < xs.Length && xs[i] <= lastX)
        {
            i++;
        }

        return i;
    }
}
