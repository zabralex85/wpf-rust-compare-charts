using System;
using TelemetryPoc.Application;

namespace TelemetryPoc.Infrastructure;

public sealed class SystemClock : ISystemClock
{
    public long UtcNowUnixMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
