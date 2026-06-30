using TelemetryPoc.Domain;

namespace TelemetryPoc.Presentation;

public static class StatusCounts
{
    public static (int Alarms, int Cautions) Compute(
        IReadOnlyList<ChannelMeta> channels,
        Func<long, double?> latest,
        IReadOnlyDictionary<(long, long), EnumValue> enumIndex)
    {
        int alarms = 0;
        foreach (var ch in channels)
        {
            if (ch.Type != "enum") continue;
            var v = latest(ch.Id);
            if (v is null) continue;
            if (ValueFormat.DecodeEnum(ch.Id, v.Value, enumIndex)?.Severity == "critical") alarms++;
        }
        return (alarms, 0);
    }
}
