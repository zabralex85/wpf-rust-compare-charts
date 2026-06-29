using System.ComponentModel;
using TelemetryPoc.App.Viz;

namespace TelemetryPoc.App.ViewModels;

/// <summary>One cell in the dashboard grid. Holds the inner content VM
/// (Gauge/Line/Map) plus its grid placement; exposes pixel geometry for the Canvas.</summary>
public sealed class WidgetViewModel : INotifyPropertyChanged
{
    public string Id { get; }
    public string Name { get; }

    public WidgetViewModel(string id, WidgetKind kind, object content, int col, int row, int cols, int rows, string name = "")
    {
        Id = id; Name = name; _kind = kind; _content = content;
        _col = col; _row = row; _cols = cols; _rows = rows;
    }

    private WidgetKind _kind;
    public WidgetKind Kind
    {
        get => _kind;
        set { _kind = value; Raise(nameof(Kind)); Raise(nameof(IsToggleable)); Raise(nameof(IsRemovable)); }
    }

    private object _content;
    public object Content { get => _content; set { _content = value; Raise(nameof(Content)); } }

    private int _col, _row, _cols, _rows;
    public int Col { get => _col; set { _col = value; Raise(nameof(Col)); Raise(nameof(Left)); } }
    public int Row { get => _row; set { _row = value; Raise(nameof(Row)); Raise(nameof(Top)); } }
    public int Cols { get => _cols; set { _cols = value; Raise(nameof(Cols)); Raise(nameof(Width)); } }
    public int Rows { get => _rows; set { _rows = value; Raise(nameof(Rows)); Raise(nameof(Height)); } }

    // Canvas geometry in DIPs: Left/Top on pitch, size = cells*158 + gaps*10 = 168*n - 10.
    public double Left => (_col - 1) * WidgetLayout.Pitch;
    public double Top => (_row - 1) * WidgetLayout.Pitch;
    public double Width => _cols * WidgetLayout.Pitch - WidgetLayout.Gap;
    public double Height => _rows * WidgetLayout.Pitch - WidgetLayout.Gap;

    public bool IsToggleable => _kind != WidgetKind.Map;
    public bool IsRemovable => _kind != WidgetKind.Map;
    public string ToggleLabel => _kind == WidgetKind.Gauge ? "LINE" : "GAUGE";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        if (n == nameof(Kind)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToggleLabel)));
    }
}
