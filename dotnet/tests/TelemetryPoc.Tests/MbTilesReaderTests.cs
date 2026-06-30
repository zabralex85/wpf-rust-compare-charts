using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using TelemetryPoc.Infrastructure;

namespace TelemetryPoc.Tests;

public class MbTilesReaderTests : IDisposable
{
	private readonly string _path = Path.Combine(Path.GetTempPath(), $"mbtest-{Guid.NewGuid():N}.mbtiles");

	public MbTilesReaderTests()
	{
		using var c = new SqliteConnection($"Data Source={_path}");
		c.Open();
		Exec(c, "CREATE TABLE tiles(zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB)");
		// gzip("hello") at z1, col0, TMS row0  → XYZ (1,0,1)
		Insert(c, 1, 0, 0, Gzip(Encoding.UTF8.GetBytes("hello")));
		// uncompressed "raw" at z1, col1, TMS row0 → XYZ (1,1,1)
		Insert(c, 1, 1, 0, Encoding.UTF8.GetBytes("raw"));
	}

	[Fact]
	public void Reads_and_gunzips_with_y_flip()
	{
		using var r = new MbTilesTileSource(_path);
		var bytes = r.Read(1, 0, 1); // y1 → TMS row (1<<1)-1-1 = 0
		Assert.Equal("hello", Encoding.UTF8.GetString(bytes!));
	}

	[Fact]
	public void Returns_uncompressed_blob_unchanged()
	{
		using var r = new MbTilesTileSource(_path);
		Assert.Equal("raw", Encoding.UTF8.GetString(r.Read(1, 1, 1)!));
	}

	[Fact]
	public void Missing_tile_returns_null()
	{
		using var r = new MbTilesTileSource(_path);
		Assert.Null(r.Read(1, 5, 5));
	}

	private static void Exec(SqliteConnection c, string sql)
	{
		using var cmd = c.CreateCommand();
		cmd.CommandText = sql; cmd.ExecuteNonQuery();
	}

	private static void Insert(SqliteConnection c, int z, int col, int row, byte[] data)
	{
		using var cmd = c.CreateCommand();
		cmd.CommandText = "INSERT INTO tiles VALUES($z,$col,$row,$d)";
		cmd.Parameters.AddWithValue("$z", z);
		cmd.Parameters.AddWithValue("$col", col);
		cmd.Parameters.AddWithValue("$row", row);
		cmd.Parameters.AddWithValue("$d", data);
		cmd.ExecuteNonQuery();
	}

	private static byte[] Gzip(byte[] d)
	{
		using var ms = new MemoryStream();
		using (var gz = new GZipStream(ms, CompressionMode.Compress))
		{
			gz.Write(d, 0, d.Length);
		}

		return ms.ToArray();
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools(); // release the file handle before delete
		if (File.Exists(_path))
		{
			File.Delete(_path);
		}
	}
}