using System.Collections.ObjectModel;
using TelemetryPoc.Domain;

namespace TelemetryPoc.App.ViewModels;

public sealed class ParamGroupViewModel
{
    public string Name { get; }
    public ObservableCollection<ParamRowViewModel> Rows { get; } = [];

    public ParamGroupViewModel(string name, IReadOnlyList<ChannelMeta> channels)
    {
        Name = name;
        foreach (var ch in channels)
        {
            Rows.Add(new ParamRowViewModel(ch));
        }
    }

    public void Refresh(TelemetryStore store)
    {
        foreach (var r in Rows)
        {
            r.Refresh(store);
        }
    }
}
