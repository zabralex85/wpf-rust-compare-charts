using System.ComponentModel;
using Avalonia.Media;
using TelemetryPoc.Domain;
using TelemetryPoc.Presentation;

namespace TelemetryPoc.App.ViewModels;

public sealed class ParamRowViewModel : INotifyPropertyChanged
{
    private readonly ChannelMeta _ch;
    public ParamRowViewModel(ChannelMeta ch) { _ch = ch; }

    public string Name => _ch.Name;
    public string Unit => _ch.Unit;
    public string Addr => _ch.Addr;
    public int ChannelId => (int)_ch.Id; // _ch.Id is long; ids are small, Rust uses a JS number

    private string _value = "—";
    public string Value { get => _value; private set { _value = value; Raise(nameof(Value)); } }

    private IBrush _valueBrush = Brushes.Gray;
    public IBrush ValueBrush { get => _valueBrush; private set { _valueBrush = value; Raise(nameof(ValueBrush)); } }

    private IBrush _dotBrush = Brushes.Gray;
    public IBrush DotBrush { get => _dotBrush; private set { _dotBrush = value; Raise(nameof(DotBrush)); } }

    private IBrush _rowBg = Brushes.Transparent;
    public IBrush RowBackground { get => _rowBg; private set { _rowBg = value; Raise(nameof(RowBackground)); } }

    private static readonly IBrush CriticalBg = Hex("#FF1A0E11");

    public void Refresh(TelemetryStore store)
    {
        var d = ParamRowView.Build(_ch, store.Latest(_ch.Id), store.EnumIndex);
        Value = d.Text;
        ValueBrush = Hex(d.ValueColor);
        DotBrush = Hex(d.DotColor);
        RowBackground = d.Critical ? CriticalBg : Brushes.Transparent;
    }

    private static IBrush Hex(string hex) => new SolidColorBrush(Color.Parse(hex));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
