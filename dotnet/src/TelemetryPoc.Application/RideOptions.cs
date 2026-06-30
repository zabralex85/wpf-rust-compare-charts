// dotnet/src/TelemetryPoc.Application/RideOptions.cs
namespace TelemetryPoc.Application;

public sealed class RideOptions
{
    public string? DbPath { get; set; }
    public double Speed { get; set; } = 1.0;
    public string? MbTilesPath { get; set; }
}
