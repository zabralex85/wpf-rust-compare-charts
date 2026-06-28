using System.ComponentModel;
using System.Windows.Media;
using TelemetryPoc.App.Viz;
using TelemetryPoc.Core;

namespace TelemetryPoc.App.ViewModels;

public sealed class ParamRowViewModel : INotifyPropertyChanged
{
    private readonly ChannelMeta _ch;
    public ParamRowViewModel(ChannelMeta ch) { _ch = ch; }

    public string Name => _ch.Name;
    public string Unit => _ch.Unit;
    public string Addr => _ch.Addr;

    private string _value = "—";
    public string Value { get => _value; private set { _value = value; Raise(nameof(Value)); } }

    private Brush _valueBrush = Brushes.Gray;
    public Brush ValueBrush { get => _valueBrush; private set { _valueBrush = value; Raise(nameof(ValueBrush)); } }

    private Brush _dotBrush = Brushes.Gray;
    public Brush DotBrush { get => _dotBrush; private set { _dotBrush = value; Raise(nameof(DotBrush)); } }

    private Brush _rowBg = Brushes.Transparent;
    public Brush RowBackground { get => _rowBg; private set { _rowBg = value; Raise(nameof(RowBackground)); } }

    private static readonly Brush CriticalBg = Hex("#FF1A0E11");

    public void Refresh(TelemetryStore store)
    {
        var d = ParamRowView.Build(_ch, store.Latest(_ch.Id), store.EnumIndex);
        Value = d.Text;
        ValueBrush = Hex(d.ValueColor);
        DotBrush = Hex(d.DotColor);
        RowBackground = d.Critical ? CriticalBg : Brushes.Transparent;
    }

    private static Brush Hex(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        b.Freeze();
        return b;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
