using TelemetryPoc.App.Viz;
using TelemetryPoc.Core;

namespace TelemetryPoc.App.ViewModels;

public sealed class LineChartViewModel
{
    private const long WindowMs = 60_000;
    private readonly ChannelMeta _ch;
    public LineChartViewModel(ChannelMeta ch) { _ch = ch; }

    public string Name => _ch.Name;
    public string Unit => _ch.Unit;

    public double[] XsSeconds { get; private set; } = Array.Empty<double>();
    public double[] Ys { get; private set; } = Array.Empty<double>();
    public double WindowMin { get; private set; }
    public double WindowMax { get; private set; }

    public event Action? Updated;
    public event Action? Reset;
    public void RaiseReset() => Reset?.Invoke();

    public void Refresh(TelemetryStore store)
    {
        var (xs, ys) = store.Series(_ch.Id)?.Arrays() ?? ((IReadOnlyList<long>)Array.Empty<long>(), (IReadOnlyList<double>)Array.Empty<double>());
        XsSeconds = LineData.ToSeconds(xs);
        var yy = new double[ys.Count];
        for (int i = 0; i < yy.Length; i++) yy[i] = ys[i];
        Ys = yy;
        var (min, max) = LineData.ScrollWindow(xs, WindowMs);
        WindowMin = min; WindowMax = max;
        Updated?.Invoke();
    }
}
