// dotnet/src/TelemetryPoc.Application/RideOptions.cs
namespace TelemetryPoc.Application;

public sealed class RideOptions
{
    public string? DbPath { get; set; }
    public double Speed { get; set; } = 1.0;
    public string? MbTilesPath { get; set; }

    /// <summary>Perf-HUD frame-loop cap in fps. 0 = uncapped free-run (default; vsync-paced and
    /// cheap on a GPU, and the honest max-FPS benchmark). Set to e.g. 30 on a software renderer
    /// (VirtualBox) to throttle the expensive CPU composites.</summary>
    public int FpsCap { get; set; }
}
