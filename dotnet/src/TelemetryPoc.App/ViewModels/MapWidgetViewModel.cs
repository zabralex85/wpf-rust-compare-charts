using TelemetryPoc.Application;
using TelemetryPoc.Domain;

namespace TelemetryPoc.App.ViewModels;

public sealed class MapWidgetViewModel
{
    private readonly RideSession _session;
    public ITileSource Tiles { get; }

    public MapWidgetViewModel(RideSession session, ITileSource tiles)
    {
        _session = session;
        Tiles = tiles;
    }

    public Region? Region { get; private set; }

    public event Action? Updated;
    public event Action? Reset;
    public void RaiseReset() { _lastTrackLen = -1; Reset?.Invoke(); }

    private int _lastTrackLen = -1;

    public (IReadOnlyList<double> Lat, IReadOnlyList<double> Lon) Track
        => _session.Store.GpsTrack();

    /// <summary>Compute the static region from the WHOLE-RIDE GPS bbox once a viewport
    /// size is known and the bounds are available. Waits (does not freeze) until then.</summary>
    public void EnsureRegion(double width, double height)
    {
        if (Region is not null || width < 1 || height < 1)
        {
            return;
        }

        var b = _session.GpsBounds;
        if (b is null)
        {
            return; // bounds set in RideSession.StartAsync; wait rather than freeze wrong
        }

        var (cLat, cLon, z) = TileMath.FitBbox(b.Value.MinLat, b.Value.MinLon, b.Value.MaxLat, b.Value.MaxLon, width, height);
        Region = new Region(cLat, cLon, z, width, height);
    }

    /// <summary>Whole-ride GPS bbox for the double-click re-fit, or null until known.</summary>
    public (double MinLat, double MinLon, double MaxLat, double MaxLon)? GpsBoundsForRefit() => _session.GpsBounds;

    /// <summary>Replace the viewport (interactive pan/zoom owns it after the first fit).</summary>
    public void SetRegion(Region r) => Region = r;

    public void Tick()
    {
        // Repaint only when the GPS track actually grew; a static map costs nothing.
        var (lat, _) = _session.Store.GpsTrack();
        if (lat.Count == _lastTrackLen)
        {
            return;
        }

        _lastTrackLen = lat.Count;
        Updated?.Invoke();
    }
}
