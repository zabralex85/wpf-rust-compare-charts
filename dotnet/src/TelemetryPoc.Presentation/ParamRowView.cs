using TelemetryPoc.Domain;

namespace TelemetryPoc.Presentation;

public sealed record ParamRowDisplay(string Text, string ValueColor, string DotColor, bool Critical);

public static class ParamRowView
{
    private const string TextColor = "#FFC3CCD8";
    private const string Dim = "#FF566273";
    private const string Green = "#FF2FD17A";
    private const string Red = "#FFFF4D52";

    public static ParamRowDisplay Build(ChannelMeta ch, double? latest,
        IReadOnlyDictionary<(long, long), EnumValue> index)
    {
        if (latest is null)
            return new ParamRowDisplay("—", Dim, Dim, false);

        var text = ValueFormat.FormatValue(ch, latest.Value, index);
        string? severity = ch.Type == "enum"
            ? ValueFormat.DecodeEnum(ch.Id, latest.Value, index)?.Severity
            : "ok";
        var critical = severity == "critical";
        var dot = Severity.Hex(severity);
        var valueColor = critical ? Red
            : severity == "ok" && ch.Type == "enum" ? Green
            : TextColor;
        return new ParamRowDisplay(text, valueColor, dot, critical);
    }
}
