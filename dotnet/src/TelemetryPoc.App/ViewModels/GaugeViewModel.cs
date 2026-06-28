using System.ComponentModel;
using TelemetryPoc.App.Viz;
using TelemetryPoc.Core;

namespace TelemetryPoc.App.ViewModels;

public sealed class GaugeViewModel : INotifyPropertyChanged
{
    private readonly ChannelMeta _ch;
    public GaugeViewModel(ChannelMeta ch) { _ch = ch; }

    public string Name => _ch.Name;
    public string Unit => _ch.Unit;

    private double _angle = -135;
    public double AngleDeg { get => _angle; private set { _angle = value; Raise(nameof(AngleDeg)); } }

    private string _value = "—";
    public string ValueText { get => _value; private set { _value = value; Raise(nameof(ValueText)); } }

    private string _min = "", _q1 = "", _q3 = "", _max = "";
    public string MinLabel { get => _min; private set { _min = value; Raise(nameof(MinLabel)); } }
    public string Q1Label { get => _q1; private set { _q1 = value; Raise(nameof(Q1Label)); } }
    public string Q3Label { get => _q3; private set { _q3 = value; Raise(nameof(Q3Label)); } }
    public string MaxLabel { get => _max; private set { _max = value; Raise(nameof(MaxLabel)); } }

    public void Refresh(TelemetryStore store)
    {
        var v = store.Latest(_ch.Id) ?? _ch.Min;
        var g = GaugeViz.Compute(v);
        AngleDeg = g.AngleDeg;
        ValueText = GaugeFormat.GaugeValue(v);
        MinLabel = g.Min; Q1Label = g.Q1; Q3Label = g.Q3; MaxLabel = g.Max;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
