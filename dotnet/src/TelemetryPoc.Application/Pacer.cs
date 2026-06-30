namespace TelemetryPoc.Application;

public sealed class Pacer
{
    private readonly double _speed;
    public Pacer(double speed) => _speed = speed <= 0 ? 1.0 : speed;
    public long DueOffsetMs(long sampleTsMs) => (long)(sampleTsMs / _speed);
    public long WaitMs(long sampleTsMs, long elapsedMs) => Math.Max(0, DueOffsetMs(sampleTsMs) - elapsedMs);
}
