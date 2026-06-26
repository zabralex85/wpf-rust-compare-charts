using System.Globalization;

namespace TelemetryPoc.Core;

public static class ValueFormat
{
    public static IReadOnlyDictionary<(long ChannelId, long Code), EnumValue> BuildEnumIndex(IEnumerable<EnumValue> enums)
    {
        var d = new Dictionary<(long, long), EnumValue>();
        foreach (var e in enums) d[(e.ChannelId, e.Code)] = e;
        return d;
    }

    public static EnumValue? DecodeEnum(long channelId, double value, IReadOnlyDictionary<(long, long), EnumValue> index)
        => index.TryGetValue((channelId, (long)Math.Floor(value + 0.5)), out var e) ? e : null;

    public static string FormatValue(ChannelMeta ch, double value, IReadOnlyDictionary<(long, long), EnumValue> index)
        => ch.Type switch
        {
            "enum" => DecodeEnum(ch.Id, value, index)?.Label ?? Math.Floor(value + 0.5).ToString(CultureInfo.InvariantCulture),
            "real" => value.ToString("F3", CultureInfo.InvariantCulture),
            _ => value.ToString(CultureInfo.InvariantCulture),
        };

    public static string SeverityColor(string? severity)
        => severity switch { "critical" => "#d22", "ok" => "#2a2", _ => "#888" };
}
