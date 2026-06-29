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
    public DashboardViewModel Dashboard { get; }

    private string _channelCountText = "ALL · 0 CH";
    public string ChannelCountText
    {
        get => _channelCountText;
        private set { _channelCountText = value; Raise(nameof(ChannelCountText)); }
    }

    public OverviewViewModel(RideSession session)
    {
        _session = session;
        Dashboard = new DashboardViewModel(session);
        _session.MetaLoaded += BuildGroups;
        _session.Ticked += RefreshRows;
    }

    private void BuildGroups()
    {
        Groups.Clear();
        var store = _session.Store;
        foreach (var g in ParamGrouping.Group(store.Channels))
            Groups.Add(new ParamGroupViewModel(g.Name, g.Channels));
        ChannelCountText = string.Format(CultureInfo.InvariantCulture, "ALL · {0} CH", store.Channels.Count);
        RefreshRows();
    }

    private void RefreshRows()
    {
        foreach (var g in Groups) g.Refresh(_session.Store);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
