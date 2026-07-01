using Microsoft.Data.Sqlite;
using TelemetryPoc.Infrastructure;

namespace TelemetryPoc.Tests;

public class MbTilesTileSourceExtraTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"mbdecode-{Guid.NewGuid():N}.mbtiles");

    [Fact]
    public void Null_path_disables_the_source()
    {
        using var r = new MbTilesTileSource(null);
        Assert.Null(r.Decoded(0, 0, 0));
    }

    [Fact]
    public void Empty_path_disables_the_source()
    {
        using var r = new MbTilesTileSource("");
        Assert.Null(r.Decoded(0, 0, 0));
    }

    [Fact]
    public void Missing_file_disables_the_source()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.mbtiles");
        using var r = new MbTilesTileSource(missing);
        Assert.Null(r.Decoded(0, 0, 0)); // _conn stays null → the File.Exists guard short-circuits before Open()
    }

    [Fact]
    public void Missing_tiles_table_surfaces_the_sqlite_exception_from_Read()
    {
        // The ctor's Open() succeeds even for a schema-mismatched db (sqlite validates the
        // header lazily); Decoded() only wraps DecodeTile in try/catch, not Read(), so a
        // missing "tiles" table propagates as a real SqliteException.
        using (var c = new SqliteConnection($"Data Source={_path}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TABLE not_tiles(x)";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        using var r = new MbTilesTileSource(_path);
        Assert.Throws<SqliteException>(() => r.Decoded(0, 0, 0));
    }

    [Fact]
    public void Decoded_returns_null_on_missing_tile_and_is_memoised()
    {
        CreateEmptyMbTiles();
        using var r = new MbTilesTileSource(_path);
        Assert.Null(r.Decoded(5, 5, 5));
        Assert.Null(r.Decoded(5, 5, 5)); // second call hits the memoised-null cache branch
    }

    [Fact]
    public void Decoded_parses_a_real_mvt_tile_into_features()
    {
        CreateMbTilesWithTile(MvtBytes.MinimalTile("water", "class", "water"));
        using var r = new MbTilesTileSource(_path);

        var feats = r.Decoded(1, 0, 1); // z1,x0,TMS-row0 → XYZ y1 (see MbTilesReaderTests for the flip)
        Assert.NotNull(feats);
        var f = Assert.Single(feats!);
        Assert.Equal("water", f.SourceLayer);
        Assert.Equal(TelemetryPoc.Domain.MvtGeomType.Point, f.Type);
        Assert.Equal(256, f.Extent);
        Assert.Single(f.Rings);
        Assert.Equal((5L, 5L), f.Rings[0][0]);
        Assert.Equal("water", f.Props["class"]);

        // second call exercises the populated-cache branch
        var feats2 = r.Decoded(1, 0, 1);
        Assert.Same(feats, feats2);
    }

    [Fact]
    public void Decoded_catches_garbage_bytes_and_caches_null()
    {
        CreateMbTilesWithTile([0xFF, 0x00, 0xFF, 0x00, 0x01]); // not valid protobuf
        using var r = new MbTilesTileSource(_path);
        Assert.Null(r.Decoded(1, 0, 1));
        Assert.Null(r.Decoded(1, 0, 1));
    }

    [Fact]
    public void Dispose_on_a_disabled_source_does_not_throw()
    {
        var r = new MbTilesTileSource(null);
        var ex = Record.Exception(r.Dispose);
        Assert.Null(ex);
    }

    private void CreateEmptyMbTiles()
    {
        using var c = new SqliteConnection($"Data Source={_path}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "CREATE TABLE tiles(zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB)";
        cmd.ExecuteNonQuery();
    }

    private void CreateMbTilesWithTile(byte[] data)
    {
        using var c = new SqliteConnection($"Data Source={_path}");
        c.Open();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE tiles(zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB)";
            cmd.ExecuteNonQuery();
        }

        using var ins = c.CreateCommand();
        ins.CommandText = "INSERT INTO tiles VALUES(1,0,0,$d)"; // z1,x0,TMS-row0 → XYZ y=(2-1-0)=1
        ins.Parameters.AddWithValue("$d", data);
        ins.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
