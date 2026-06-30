namespace TelemetryPoc.Domain;

/// <summary>The .NET app drives its own replay clock. Pause freezes it; seek jumps it.
/// Speed scaling happens in the caller (delta × speed) before <see cref="Advance"/>.</summary>
public sealed class RideClock
{
    public long RideMs { get; private set; }
    public bool Playing { get; private set; } = true;

    public void Advance(long scaledDeltaMs)
    {
        if (!Playing || scaledDeltaMs <= 0)
        {
            return;
        }

        RideMs += scaledDeltaMs;
    }

    public void Pause() => Playing = false;
    public void Resume() => Playing = true;

    public long SeekTo(long targetMs, long durationMs)
    {
        var max = durationMs > 0 ? durationMs : 0;
        RideMs = Math.Clamp(targetMs, 0, max);
        return RideMs;
    }
}
