using System.IO.Compression;
using Mapbox.VectorTile;
using Mapbox.VectorTile.Geometry;
using Microsoft.Data.Sqlite;
using TelemetryPoc.Application;
using TelemetryPoc.Domain;

namespace TelemetryPoc.Infrastructure;

public sealed class MbTilesTileSource : ITileSource
{
    private readonly SqliteConnection? _conn;
    private readonly Dictionary<long, IReadOnlyList<MapFeature>?> _decodeCache = [];

    public MbTilesTileSource(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try { _conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly"); _conn.Open(); }
            catch { _conn?.Dispose(); _conn = null; }
        }
    }

    /// <summary>Read + gunzip + MVT-decode a tile, memoised. The sqlite read, gunzip
    /// and protobuf decode are the per-frame cost that froze interactive pan/zoom;
    /// caching the decoded features makes pan reuse tiles and zoom decode each level once.</summary>
    public IReadOnlyList<MapFeature>? Decoded(int z, int x, int y)
    {
        if (_conn is null)
        {
            return null;
        }

        long key = ((long)z << 42) ^ ((long)x << 21) ^ (uint)y;
        if (_decodeCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        IReadOnlyList<MapFeature>? feats = null;
        var bytes = Read(z, x, y);
        if (bytes is not null)
        {
            try { feats = DecodeTile(bytes); } catch { feats = null; }
        }
        _decodeCache[key] = feats;
        return feats;
    }

    public byte[]? Read(int z, int x, int y)
    {
        var row = (1 << z) - 1 - y;
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = "SELECT tile_data FROM tiles WHERE zoom_level=$z AND tile_column=$x AND tile_row=$row LIMIT 1";
        cmd.Parameters.AddWithValue("$z", z);
        cmd.Parameters.AddWithValue("$x", x);
        cmd.Parameters.AddWithValue("$row", row);
        if (cmd.ExecuteScalar() is not byte[] blob)
        {
            return null;
        }

        return Gunzip(blob);
    }

    private static byte[] Gunzip(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b)
        {
            using var input = new MemoryStream(data);
            using var gz = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gz.CopyTo(output);
            return output.ToArray();
        }
        return data;
    }

    private static IReadOnlyList<MapFeature> DecodeTile(byte[] mvtBytes)
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
                var feat = layer.GetFeature(i);
                var type = feat.GeometryType switch
                {
                    GeomType.POINT => MvtGeomType.Point,
                    GeomType.LINESTRING => MvtGeomType.Line,
                    GeomType.POLYGON => MvtGeomType.Polygon,
                    _ => MvtGeomType.Unknown,
                };
                var rings = new List<IReadOnlyList<(long X, long Y)>>();
                // API deviation: Geometry<T>(Nullable<uint> clipBuffer, Nullable<float> scale) — generic method with two params
                foreach (var ring in feat.Geometry<long>())
                {
                    var pts = new List<(long X, long Y)>(ring.Count);
                    // Point2d<T>.X/.Y are public fields (not properties)
                    foreach (var p in ring)
                    {
                        pts.Add((p.X, p.Y));
                    }

                    rings.Add(pts);
                }
                var props = new Dictionary<string, string>();
                // API deviation: GetProperties() returns Dictionary<string, object>; .ToString() converts values
                foreach (var kv in feat.GetProperties())
                {
                    props[kv.Key] = kv.Value?.ToString() ?? "";
                }

                result.Add(new MapFeature(layerName, type, rings, props, extent));
            }
        }
        return result;
    }

    public void Dispose() => _conn?.Dispose();
}
