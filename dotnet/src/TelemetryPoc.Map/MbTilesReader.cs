using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace TelemetryPoc.Map;

public sealed class MbTilesReader : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly Dictionary<long, IReadOnlyList<MapFeature>?> _decodeCache = new();

    public MbTilesReader(string path)
    {
        _conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        _conn.Open();
    }

    /// <summary>Read + gunzip + MVT-decode a tile, memoised. The sqlite read, gunzip
    /// and protobuf decode are the per-frame cost that froze interactive pan/zoom;
    /// caching the decoded features makes pan reuse tiles and zoom decode each level once.</summary>
    public IReadOnlyList<MapFeature>? Decoded(int z, int x, int y)
    {
        long key = ((long)z << 42) ^ ((long)x << 21) ^ (uint)y;
        if (_decodeCache.TryGetValue(key, out var cached)) return cached;
        IReadOnlyList<MapFeature>? feats = null;
        var bytes = Read(z, x, y);
        if (bytes is not null)
        {
            try { feats = MvtBasemap.DecodeTile(bytes); } catch { feats = null; }
        }
        _decodeCache[key] = feats;
        return feats;
    }

    public byte[]? Read(int z, int x, int y)
    {
        var row = (1 << z) - 1 - y;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT tile_data FROM tiles WHERE zoom_level=$z AND tile_column=$x AND tile_row=$row LIMIT 1";
        cmd.Parameters.AddWithValue("$z", z);
        cmd.Parameters.AddWithValue("$x", x);
        cmd.Parameters.AddWithValue("$row", row);
        if (cmd.ExecuteScalar() is not byte[] blob) return null;
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

    public void Dispose() => _conn.Dispose();
}
