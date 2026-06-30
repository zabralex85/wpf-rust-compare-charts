using TelemetryPoc.Domain;

namespace TelemetryPoc.Application;

/// <summary>Ride metadata, ready to replay. Samples are streamed via ISampleCursor, not
/// carried here, so a large ride costs no extra memory to "load".</summary>
public sealed record RideData(
    IReadOnlyList<ChannelMeta> Channels,
    IReadOnlyList<EnumValue> Enums,
    long DurationMs,
    (double MinLat, double MinLon, double MaxLat, double MaxLon)? GpsBounds);
