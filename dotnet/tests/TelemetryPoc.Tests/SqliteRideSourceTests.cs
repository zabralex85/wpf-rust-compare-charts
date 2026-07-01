using Microsoft.Data.Sqlite;
using TelemetryPoc.Application;
using TelemetryPoc.Infrastructure;

namespace TelemetryPoc.Tests;

internal sealed class FakeRidePathResolver : IRidePathResolver
{
    private readonly string _dbPath;
    private readonly string? _mbTiles;
    public FakeRidePathResolver(string dbPath, string? mbTiles = null) { _dbPath = dbPath; _mbTiles = mbTiles; }
    public string ResolveRideDb() => _dbPath;
    public string? ResolveMbTiles() => _mbTiles;
}

public class SqliteRideSourceTests
{
    [Fact]
    public async Task LoadAsync_loads_channels_enums_duration_and_gps_bounds()
    {
        var source = new SqliteRideSource(new FakeRidePathResolver(Fixtures.RideSmallDb()));
        var data = await source.LoadAsync();

        Assert.NotEmpty(data.Channels);
        Assert.NotEmpty(data.Enums);
        Assert.Equal(10_000, data.DurationMs); // ride_small.db: duration_s=10
        Assert.NotNull(data.GpsBounds);
        var (minLat, minLon, maxLat, maxLon) = data.GpsBounds!.Value;
        Assert.True(minLat <= maxLat);
        Assert.True(minLon <= maxLon);
    }

    [Fact]
    public void OpenSamples_streams_every_row_in_order()
    {
        var source = new SqliteRideSource(new FakeRidePathResolver(Fixtures.RideSmallDb()));
        using var cur = source.OpenSamples();

        int n = 0;
        long last = -1;
        while (cur.PeekTs is { } t)
        {
            Assert.True(t > last);
            cur.Read();
            last = t;
            n++;
        }

        Assert.Equal(100, n);
    }

    [Fact]
    public async Task GpsBounds_is_null_when_ride_has_no_map_channels()
    {
        var path = MiniDb.Create(includeGpsChannels: false, populateSamples: true);
        try
        {
            var source = new SqliteRideSource(new FakeRidePathResolver(path));
            var data = await source.LoadAsync();
            Assert.Null(data.GpsBounds);
        }
        finally
        {
            MiniDb.Cleanup(path);
        }
    }

    [Fact]
    public async Task GpsBounds_is_null_when_samples_table_is_empty()
    {
        var path = MiniDb.Create(includeGpsChannels: true, populateSamples: false);
        try
        {
            var source = new SqliteRideSource(new FakeRidePathResolver(path));
            var data = await source.LoadAsync();
            Assert.Null(data.GpsBounds);
        }
        finally
        {
            MiniDb.Cleanup(path);
        }
    }

    [Fact]
    public void LoadRideMeta_throws_when_ride_meta_table_is_empty()
    {
        var path = MiniDb.Create(includeGpsChannels: true, populateSamples: true, populateRideMeta: false);
        try
        {
            using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            conn.Open();
            Assert.Throws<InvalidOperationException>(() => SqliteRideSource.LoadRideMeta(conn));
        }
        finally
        {
            MiniDb.Cleanup(path);
        }
    }
}

/// <summary>Builds tiny hand-rolled ride DBs (schema-compatible with data/simulate.py's
/// ride.db) so SqliteRideSource's branch logic can be tested without depending on the
/// exact contents of the committed fixture.</summary>
internal static class MiniDb
{
    public static string Create(bool includeGpsChannels, bool populateSamples, bool populateRideMeta = true)
    {
        var path = Path.Combine(Path.GetTempPath(), $"minidb-{Guid.NewGuid():N}.db");
        using var c = new SqliteConnection($"Data Source={path}");
        c.Open();
        Exec(c, "CREATE TABLE channels(id INTEGER, name TEXT, column_name TEXT, unit TEXT, type TEXT, min REAL, max REAL, widget TEXT, display_order INTEGER, addr TEXT)");
        Exec(c, "CREATE TABLE enum_values(channel_id INTEGER, code INTEGER, label TEXT, severity TEXT)");
        Exec(c, "CREATE TABLE ride_meta(start_time INTEGER, duration_s INTEGER, rate_hz INTEGER, channel_count INTEGER)");

        Exec(c, "INSERT INTO channels VALUES (1,'Roll','roll','deg','real',-180,180,'strip',1,'0x1')");
        if (includeGpsChannels)
        {
            Exec(c, "INSERT INTO channels VALUES (2,'Lat','lat','deg','real',-90,90,'map_lat',2,'0x2')");
            Exec(c, "INSERT INTO channels VALUES (3,'Lon','lon','deg','real',-180,180,'map_lon',3,'0x3')");
            Exec(c, "CREATE TABLE samples(ts INTEGER PRIMARY KEY, roll REAL, lat REAL, lon REAL)");
            if (populateSamples)
            {
                Exec(c, "INSERT INTO samples VALUES (0, 1.0, 32.0, 34.8)");
                Exec(c, "INSERT INTO samples VALUES (100, 2.0, 32.1, 34.9)");
            }
        }
        else
        {
            Exec(c, "CREATE TABLE samples(ts INTEGER PRIMARY KEY, roll REAL)");
            if (populateSamples)
            {
                Exec(c, "INSERT INTO samples VALUES (0, 1.0)");
                Exec(c, "INSERT INTO samples VALUES (100, 2.0)");
            }
        }

        if (populateRideMeta)
        {
            Exec(c, "INSERT INTO ride_meta VALUES (0, 10, 10, " + (includeGpsChannels ? 3 : 1) + ")");
        }

        return path;
    }

    public static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
