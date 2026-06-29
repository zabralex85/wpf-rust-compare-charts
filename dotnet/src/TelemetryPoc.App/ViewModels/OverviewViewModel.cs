using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using TelemetryPoc.App.Viz;
using TelemetryPoc.Core;

namespace TelemetryPoc.App.ViewModels;

public sealed class OverviewViewModel : INotifyPropertyChanged
{
    private readonly RideSession _session;
    public ObservableCollection<ParamGroupViewModel> Groups { get; } = new();
    public ObservableCollection<GaugeViewModel> Gauges { get; } = new();
    public ObservableCollection<LineChartViewModel> LineCharts { get; } = new();
    public MapWidgetViewModel MapWidget { get; }

    private string _channelCountText = "ALL · 0 CH";
    public string ChannelCountText
    {
        get => _channelCountText;
        private set { _channelCountText = value; Raise(nameof(ChannelCountText)); }
    }

    public OverviewViewModel(RideSession session)
    {
        _session = session;
        _session.MetaLoaded += BuildGroups;
        _session.Ticked += RefreshRows;
        MapWidget = new MapWidgetViewModel(session);
        session.Ticked += MapWidget.Tick;
    }

    private void BuildGroups()
    {
        Groups.Clear();
        var store = _session.Store;
        foreach (var g in ParamGrouping.Group(store.Channels))
            Groups.Add(new ParamGroupViewModel(g.Name, g.Channels));
        ChannelCountText = string.Format(CultureInfo.InvariantCulture, "ALL · {0} CH", store.Channels.Count);
        Gauges.Clear();
        foreach (var ch in store.Channels)
            if (ch.Widget == "gauge") Gauges.Add(new GaugeViewModel(ch));
        LineCharts.Clear();
        foreach (var ch in store.Channels)
            if (ch.Widget == "strip") LineCharts.Add(new LineChartViewModel(ch));
        RefreshRows();
    }

    private void RefreshRows()
    {
        foreach (var g in Groups) g.Refresh(_session.Store);
        foreach (var g in Gauges) g.Refresh(_session.Store);
        foreach (var lc in LineCharts) lc.Refresh(_session.Store);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
