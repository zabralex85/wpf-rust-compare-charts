using System.Collections.Generic;
using TelemetryPoc.Core;

namespace TelemetryPoc.App.Viz;

/// <summary>An entire loaded ride, ready to replay. Loading (sqlite, GPS-bounds
/// computation) is the host's job; the engine only orchestrates this prepared data,
/// which keeps it UI- and IO-free and therefore testable.</summary>
public sealed record RideData(
    IReadOnlyList<ChannelMeta> Channels,
    IReadOnlyList<EnumValue> Enums,
    IReadOnlyList<Sample> Samples,
    long DurationMs,
    (double MinLat, double MinLon, double MaxLat, double MaxLon)? GpsBounds);
