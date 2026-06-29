using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace TelemetryPoc.Map;

public sealed class MbTilesReader : IDisposable
{
    private readonly SqliteConnection _conn;

    public MbTilesReader(string path)
    {
        _conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        _conn.Open();
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
