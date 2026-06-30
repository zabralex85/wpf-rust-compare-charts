using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

    public static RideData Load(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        var channels = LoadChannels(conn);
        var enums = LoadEnumValues(conn);
        var samples = LoadSamples(conn, channels);
        var meta = LoadRideMeta(conn);
        return new RideData(channels, enums, samples, meta.DurationS * 1000, GpsBounds(channels, samples));
    }

    private static (double, double, double, double)? GpsBounds(
        IReadOnlyList<ChannelMeta> channels, IReadOnlyList<Sample> samples)
    {
        int latIdx = -1, lonIdx = -1;
        for (int i = 0; i < channels.Count; i++)
        {
            if (channels[i].Widget == "map_lat") latIdx = i;
            if (channels[i].Widget == "map_lon") lonIdx = i;
        }
        if (latIdx < 0 || lonIdx < 0 || samples.Count == 0) return null;
        var lat = new double[samples.Count];
        var lon = new double[samples.Count];
        for (int i = 0; i < samples.Count; i++) { lat[i] = samples[i].Values[latIdx]; lon[i] = samples[i].Values[lonIdx]; }
        return MapProject.TrackBounds(lat, lon);
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
        if (!r.Read()) throw new InvalidOperationException("ride_meta is empty");
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
                values[i] = Convert.ToDouble(r.GetValue(i + 1));
            list.Add(new Sample(r.GetInt64(0), values));
        }
        return list;
    }
}
