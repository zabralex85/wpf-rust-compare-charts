using System.ComponentModel;
using System.Globalization;
using TelemetryPoc.App.Viz;

namespace TelemetryPoc.App.ViewModels;

public sealed class TransportViewModel : INotifyPropertyChanged
{
    private readonly RideSession _session;
    public TransportViewModel(RideSession session)
    {
        _session = session;
        _session.Ticked += Refresh;
    }

    public string ClockText => _session.ClockText;
    public string TPlusText => _session.TPlusText;

    private double _progress;
    public double Progress { get => _progress; private set { _progress = value; Raise(nameof(Progress)); } }

    private string _buffer = "0:00:00";
    public string BufferText { get => _buffer; private set { _buffer = value; Raise(nameof(BufferText)); } }

    private string _samples = "0";
    public string SamplesText { get => _samples; private set { _samples = value; Raise(nameof(SamplesText)); } }

    public string DroppedText => "0";

    private void Refresh()
    {
        var dur = _session.DurationMs;
        Progress = dur > 0 ? Math.Max(0, Math.Min(1, (double)_session.RideMs / dur)) : 0;
        BufferText = MissionClock.FormatShort(_session.RideMs);

        var store = _session.Store;
        var maxLen = 0;
        foreach (var ch in store.Channels)
            if (ch.Widget == "strip")
            {
                var len = store.Series(ch.Id)?.Len ?? 0;
                if (len > maxLen) maxLen = len;
            }
        SamplesText = maxLen.ToString(CultureInfo.InvariantCulture);

        Raise(nameof(ClockText));
        Raise(nameof(TPlusText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
