namespace TelemetryPoc.Core.Tests;

public class MetricsSamplerTests
{
    [Fact]
    public void Sample_returns_finite_nonnegative_metrics()
    {
        var s = new MetricsSampler();
        _ = s.Sample();              // prime
        var m = s.Sample();
        Assert.True(m.RamMb > 0.0);
        Assert.True(m.CpuPct >= 0.0);
        Assert.True(double.IsFinite(m.CpuPct));
    }
}
