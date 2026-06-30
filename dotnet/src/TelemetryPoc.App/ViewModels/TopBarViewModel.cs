using System.ComponentModel;
using System.Globalization;
using TelemetryPoc.App.Viz;

namespace TelemetryPoc.App.ViewModels;

public sealed class TopBarViewModel : INotifyPropertyChanged
{
    private readonly RideSession _session;
    public TopBarViewModel(RideSession session)
    {
        _session = session;
        _session.Ticked += Refresh;
    }

    public string ClockText => MissionClock.Format(_session.RideMs);

    private string _alarm = "0 ALARM", _caution = "0 CAUTION";
    public string AlarmText { get => _alarm; private set { _alarm = value; Raise(nameof(AlarmText)); } }
    public string CautionText { get => _caution; private set { _caution = value; Raise(nameof(CautionText)); } }

    private void Refresh()
    {
        var store = _session.Store;
        var (alarms, cautions) = StatusCounts.Compute(store.Channels, store.Latest, store.EnumIndex);
        AlarmText = string.Format(CultureInfo.InvariantCulture, "{0} ALARM", alarms);
        CautionText = string.Format(CultureInfo.InvariantCulture, "{0} CAUTION", cautions);
        Raise(nameof(ClockText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
