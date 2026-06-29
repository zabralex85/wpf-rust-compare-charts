using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TelemetryPoc.App.Viz;
using TelemetryPoc.Core;

namespace TelemetryPoc.App.ViewModels;

/// <summary>Owns the dashboard widget collection. All geometry decisions delegate to
/// WidgetLayout (pure, tested); this class only instantiates content VMs and mutates
/// placement. In-memory only — rebuilt from channels on each meta load.</summary>
public sealed class DashboardViewModel
{
    private readonly RideSession _session;
    private readonly MapWidgetViewModel _map;
    private readonly Dictionary<int, ChannelMeta> _byId = new();
    private int _nextId = 1;

    public ObservableCollection<WidgetViewModel> Widgets { get; } = new();

    public DashboardViewModel(RideSession session)
    {
        _session = session;
        _map = new MapWidgetViewModel(session);
        _session.MetaLoaded += Build;
        _session.Ticked += Refresh;
        _session.Ticked += _map.Tick;
        _session.Reset += OnReset;
    }

    private void Build()
    {
        Widgets.Clear();
        _byId.Clear();
        var store = _session.Store;
        foreach (var ch in store.Channels) _byId[(int)ch.Id] = ch;

        foreach (var w in WidgetSeed.SeedLayout(store.Channels))
            Widgets.Add(new WidgetViewModel(w.Id, w.Kind, ContentFor(w.Kind, w.ChannelId), w.Col, w.Row, w.Cols, w.Rows, w.Name));
        Refresh();
    }

    private object ContentFor(WidgetKind kind, int? channelId) => kind switch
    {
        WidgetKind.Map => _map,
        WidgetKind.Line => new LineChartViewModel(_byId[channelId!.Value]),
        _ => new GaugeViewModel(_byId[channelId!.Value]),
    };

    private void Refresh()
    {
        var store = _session.Store;
        foreach (var w in Widgets)
            switch (w.Content)
            {
                case GaugeViewModel g: g.Refresh(store); break;
                case LineChartViewModel l: l.Refresh(store); break;
            }
    }

    private void OnReset()
    {
        foreach (var w in Widgets)
        {
            if (w.Content is LineChartViewModel l) l.RaiseReset();
            if (w.Content is MapWidgetViewModel m) m.RaiseReset();
        }
    }

    public void AddGauge(int channelId, int col, int row)
    {
        if (!_byId.TryGetValue(channelId, out var ch)) return;
        var id = $"w-{_nextId++}";
        var w = new WidgetViewModel(id, WidgetKind.Gauge, new GaugeViewModel(ch), col, row, 1, 1, ch.Name);
        Widgets.Add(w);
        Refresh();
    }

    public void Move(string id, int col, int row)
    {
        var w = Find(id); if (w is null) return;
        w.Col = col; w.Row = row;
    }

    public void Resize(string id, int cols, int rows)
    {
        var w = Find(id); if (w is null) return;
        var (c, r) = WidgetLayout.ClampSize(w.Kind, cols, rows);
        w.Cols = c; w.Rows = r;
    }

    public void Toggle(string id)
    {
        var w = Find(id); if (w is null || !w.IsToggleable) return;
        var content = w.Content;
        int channelId = content switch
        {
            GaugeViewModel => ChannelOf(content),
            LineChartViewModel => ChannelOf(content),
            _ => -1,
        };
        if (channelId < 0 || !_byId.TryGetValue(channelId, out var ch)) return;
        var (kind, cols, rows) = WidgetLayout.Toggle(w.Kind, w.Cols, w.Rows);
        w.Content = kind == WidgetKind.Line ? new LineChartViewModel(ch) : (object)new GaugeViewModel(ch);
        w.Kind = kind; w.Cols = cols; w.Rows = rows;
        Refresh();
    }

    public void Remove(string id)
    {
        var w = Find(id); if (w is null || !w.IsRemovable) return;
        Widgets.Remove(w);
    }

    public void ZoomBy(string id, double factor)
    {
        var w = Find(id); if (w is null) return;
        if (w.Content is LineChartViewModel l) l.ZoomBy(factor);
    }

    private WidgetViewModel? Find(string id) => Widgets.FirstOrDefault(x => x.Id == id);

    // Inner content VMs expose Name/Unit but not channel id; resolve via the metadata map.
    private int ChannelOf(object content)
    {
        var name = content switch
        {
            GaugeViewModel g => g.Name,
            LineChartViewModel l => l.Name,
            _ => null,
        };
        if (name is null) return -1;
        foreach (var ch in _byId.Values) if (ch.Name == name) return (int)ch.Id;
        return -1;
    }
}
