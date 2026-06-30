using Microsoft.Data.Sqlite;
using TelemetryPoc.Application;
using TelemetryPoc.Domain;

namespace TelemetryPoc.Infrastructure;

public sealed class SqliteRideSource : IRideSource
{
    private readonly IRidePathResolver _paths;

    public SqliteRideSource(IRidePathResolver paths) => _paths = paths;

    public Task<RideData> LoadAsync(CancellationToken ct = default) =>
        Task.Run(() => Load(_paths.ResolveRideDb()), ct);

    public ISampleCursor OpenSamples()
    {
        var dbPath = _paths.ResolveRideDb();
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        var columns = new List<string>();
        foreach (var c in LoadChannels(conn)) columns.Add(c.ColumnName);
        return new SqliteSampleCursor(dbPath, columns);
    }

    private static RideData Load(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        var channels = LoadChannels(conn);
        var enums = LoadEnumValues(conn);
        var meta = LoadRideMeta(conn);
        return new RideData(channels, enums, meta.DurationS * 1000, GpsBounds(conn, channels));
    }

    /// <summary>Whole-ride GPS bbox via a MIN/MAX aggregate — no sample materialisation.</summary>
    private static (double, double, double, double)? GpsBounds(SqliteConnection conn, IReadOnlyList<ChannelMeta> channels)
    {
        string? latCol = null, lonCol = null;
        foreach (var c in channels)
        {
            if (c.Widget == "map_lat") latCol = c.ColumnName;
            if (c.Widget == "map_lon") lonCol = c.ColumnName;
        }

        if (latCol is null || lonCol is null) return null;

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT MIN(\"{latCol}\"), MIN(\"{lonCol}\"), MAX(\"{latCol}\"), MAX(\"{lonCol}\") FROM samples";
        using var r = cmd.ExecuteReader();
        if (!r.Read() || r.IsDBNull(0)) return null; // empty samples table
        return (r.GetDouble(0), r.GetDouble(1), r.GetDouble(2), r.GetDouble(3));
    }

    public static IReadOnlyList<ChannelMeta> LoadChannels(SqliteConnection conn)
    {
        var list = new List<ChannelMeta>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, name, column_name, unit, type, min, max, widget, display_order, addr " +
            "FROM channels ORDER BY display_order";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ChannelMeta(
                r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.GetDouble(5), r.GetDouble(6), r.GetString(7), r.GetInt64(8), r.GetString(9)));
        }
        return list;
    }

    public static IReadOnlyList<EnumValue> LoadEnumValues(SqliteConnection conn)
    {
        var list = new List<EnumValue>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT channel_id, code, label, severity FROM enum_values ORDER BY channel_id, code";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new EnumValue(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3)));
        return list;
    }

    public static RideMeta LoadRideMeta(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT start_time, duration_s, rate_hz, channel_count FROM ride_meta LIMIT 1";
        using var r = cmd.ExecuteReader();
        if (!r.Read())
		{
			throw new InvalidOperationException("ride_meta is empty");
		}

        return new RideMeta(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3));
    }

    public static IReadOnlyList<Sample> LoadSamples(SqliteConnection conn, IReadOnlyList<ChannelMeta> channels)
    {
        var cols = string.Join(", ", channels.Select(c => "\"" + c.ColumnName + "\""));
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT ts, {cols} FROM samples ORDER BY ts";
        using var r = cmd.ExecuteReader();
        var list = new List<Sample>();
        var n = channels.Count;
        while (r.Read())
        {
            var values = new double[n];
            for (int i = 0; i < n; i++)
			{
				values[i] = Convert.ToDouble(r.GetValue(i + 1));
			}

            list.Add(new Sample(r.GetInt64(0), values));
        }
        return list;
    }
}
