using TelemetryPoc.Domain;
using TelemetryPoc.Presentation;

namespace TelemetryPoc.App.ViewModels;

public sealed class LineChartViewModel
{
    public int Zoom { get; private set; } = 1;
    private long WindowMs => 60_000 / Zoom;

    public void ZoomBy(double factor) => Zoom = WidgetLayout.ZoomBy(Zoom, factor);
    public void ResetZoom() => Zoom = 1;
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
        var (xs, ys) = store.Series(_ch.Id)?.Arrays() ?? (Array.Empty<long>(), Array.Empty<double>());
        XsSeconds = LineData.ToSeconds(xs);
        var yy = new double[ys.Count];
        for (int i = 0; i < yy.Length; i++)
        {
            yy[i] = ys[i];
        }

        Ys = yy;
        var (min, max) = LineData.ScrollWindow(xs, WindowMs);
        WindowMin = min; WindowMax = max;
        Updated?.Invoke();
    }
}
