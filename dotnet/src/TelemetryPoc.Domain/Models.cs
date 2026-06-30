namespace TelemetryPoc.Domain;

public sealed record ChannelMeta(
    long Id, string Name, string ColumnName, string Unit, string Type,
    double Min, double Max, string Widget, long DisplayOrder, string Addr);

public sealed record EnumValue(long ChannelId, long Code, string Label, string Severity);

public sealed record RideMeta(long StartTime, long DurationS, long RateHz, long ChannelCount);

public sealed record Sample(long TsMs, double[] Values);
