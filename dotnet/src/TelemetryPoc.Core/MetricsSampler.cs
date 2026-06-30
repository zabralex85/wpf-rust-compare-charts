using System.Diagnostics;

namespace TelemetryPoc.Core;

public sealed class MetricsSampler
{
    private readonly Process _p = Process.GetCurrentProcess();
    private TimeSpan _lastCpu;
    private DateTime _lastTime;
    private bool _primed;

    public Metrics Sample()
    {
        _p.Refresh();
        var now = DateTime.UtcNow;
        var cpu = _p.TotalProcessorTime;
        double pct = 0.0;
        if (_primed)
        {
            var dtMs = (now - _lastTime).TotalMilliseconds;
            var dcMs = (cpu - _lastCpu).TotalMilliseconds;
            // Per-core CPU% (100% = one full core), matching the Rust app's sysinfo convention for a fair comparison.
            if (dtMs > 0) pct = dcMs / dtMs * 100.0;
        }
        _lastCpu = cpu;
        _lastTime = now;
        _primed = true;
        return new Metrics(Math.Max(0.0, pct), _p.WorkingSet64 / 1_048_576.0);
    }
}
