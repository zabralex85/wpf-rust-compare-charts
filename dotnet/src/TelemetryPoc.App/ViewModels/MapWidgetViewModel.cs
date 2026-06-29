using System;
using System.IO;
using TelemetryPoc.Core;
using TelemetryPoc.Map;

namespace TelemetryPoc.App.ViewModels;

public sealed class MapWidgetViewModel
{
    private readonly RideSession _session;
    public MapWidgetViewModel(RideSession session) { _session = session; }

    public Region? Region { get; private set; }
    public string? MbTilesPath { get; private set; }
    public event Action? Updated;

    public (System.Collections.Generic.IReadOnlyList<double> Lat, System.Collections.Generic.IReadOnlyList<double> Lon) Track
        => _session.Store.GpsTrack();

    /// <summary>Compute the static region from the WHOLE-RIDE GPS bbox once a viewport
    /// size is known and the bounds are available. Waits (does not freeze) until then.</summary>
    public void EnsureRegion(double width, double height)
    {
        if (Region is not null || width < 1 || height < 1) return;
        MbTilesPath ??= ResolveMbTiles();
        var b = _session.GpsBounds;
        if (b is null) return; // bounds set in RideSession.Start; wait rather than freeze wrong
        var (cLat, cLon, z) = TileMath.FitBbox(b.Value.MinLat, b.Value.MinLon, b.Value.MaxLat, b.Value.MaxLon, width, height);
        Region = new Region(cLat, cLon, z, width, height);
    }

    public void Tick() => Updated?.Invoke();

    private static string? ResolveMbTiles()
    {
        var env = Environment.GetEnvironmentVariable("RIDE_MBTILES");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var p = Path.Combine(dir.FullName, "tiles", "israel.mbtiles");
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        return null;
    }
}
