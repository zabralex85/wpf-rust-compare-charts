namespace TelemetryPoc.Domain;

/// <summary>Pure interaction math for the Skia map: drag-pan and zoom-toward-cursor.
/// All geometry goes through Web Mercator world coordinates at the relevant zoom.</summary>
public static class MapInteract
{
    /// <summary>Shift the viewport by a screen-pixel delta. Dragging content by
    /// (dxPx,dyPx) moves the center by the opposite world delta.</summary>
    public static Region Pan(Region r, double dxPx, double dyPx)
    {
        var (cx, cy) = WebMercator.LonLatToWorld(r.CenterLon, r.CenterLat, r.Zoom);
        var (lon, lat) = WebMercator.WorldToLonLat(cx - dxPx, cy - dyPx, r.Zoom);
        return r with { CenterLat = lat, CenterLon = lon };
    }

    /// <summary>Zoom by integer `step` (clamped to [minZoom,maxZoom]) while keeping the
    /// geographic point under the cursor fixed on screen (MapLibre-style).</summary>
    public static Region ZoomAt(Region r, double cursorX, double cursorY, int step, int minZoom, int maxZoom)
    {
        var newZoom = Math.Clamp(r.Zoom + step, minZoom, maxZoom);
        if (newZoom == r.Zoom)
        {
            return r;
        }

        // Geo point currently under the cursor.
        var (cx, cy) = WebMercator.LonLatToWorld(r.CenterLon, r.CenterLat, r.Zoom);
        var cursorWorldX = cx - r.Width / 2 + cursorX;
        var cursorWorldY = cy - r.Height / 2 + cursorY;
        var (geoLon, geoLat) = WebMercator.WorldToLonLat(cursorWorldX, cursorWorldY, r.Zoom);

        // Place that geo point back under the cursor at the new zoom.
        var (gx, gy) = WebMercator.LonLatToWorld(geoLon, geoLat, newZoom);
        var newCenterX = gx - cursorX + r.Width / 2;
        var newCenterY = gy - cursorY + r.Height / 2;
        var (newLon, newLat) = WebMercator.WorldToLonLat(newCenterX, newCenterY, newZoom);
        return r with { CenterLat = newLat, CenterLon = newLon, Zoom = newZoom };
    }
}
