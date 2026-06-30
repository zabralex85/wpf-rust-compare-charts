namespace TelemetryPoc.Presentation;

/// <summary>Finds the sample index whose X is closest to a target (hover lookup).
/// Assumes xs is non-decreasing (time series).</summary>
public static class NearestSample
{
    public static int IndexOf(double[] xs, double xTarget)
    {
        if (xs.Length == 0)
        {
            return -1;
        }

        int best = 0;
        double bestDist = Math.Abs(xs[0] - xTarget);
        for (int i = 1; i < xs.Length; i++)
        {
            double d = Math.Abs(xs[i] - xTarget);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }
}
