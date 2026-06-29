using Mapbox.VectorTile;
using Mapbox.VectorTile.Geometry;

namespace TelemetryPoc.Map;

public static class MvtBasemap
{
    public static IReadOnlyList<MapFeature> DecodeTile(byte[] mvtBytes)
    {
        var result = new List<MapFeature>();
        // API deviation: constructor requires (byte[] data, bool validate)
        var reader = new VectorTileReader(mvtBytes, false);
        foreach (var layerName in reader.LayerNames())
        {
            var layer = reader.GetLayer(layerName);
            int extent = (int)layer.Extent;
            for (int i = 0; i < layer.FeatureCount(); i++)
            {
                // API deviation: GetFeature(int feature, Nullable<uint> clipBuffer, float scale)
                var feat = layer.GetFeature(i, null, 1.0f);
                var type = feat.GeometryType switch
                {
                    GeomType.POINT => MvtGeomType.Point,
                    GeomType.LINESTRING => MvtGeomType.Line,
                    GeomType.POLYGON => MvtGeomType.Polygon,
                    _ => MvtGeomType.Unknown,
                };
                var rings = new List<IReadOnlyList<(long X, long Y)>>();
                // API deviation: Geometry<T>(Nullable<uint> clipBuffer, Nullable<float> scale) — generic method with two params
                foreach (var ring in feat.Geometry<long>(null, null))
                {
                    var pts = new List<(long X, long Y)>(ring.Count);
                    // Point2d<T>.X/.Y are public fields (not properties)
                    foreach (var p in ring) pts.Add((p.X, p.Y));
                    rings.Add(pts);
                }
                var props = new Dictionary<string, string>();
                // API deviation: GetProperties() returns Dictionary<string, object>; .ToString() converts values
                foreach (var kv in feat.GetProperties())
                    props[kv.Key] = kv.Value?.ToString() ?? "";
                result.Add(new MapFeature(layerName, type, rings, props, extent));
            }
        }
        return result;
    }
}
