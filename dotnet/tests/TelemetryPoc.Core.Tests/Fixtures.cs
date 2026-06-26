using Microsoft.Data.Sqlite;

namespace TelemetryPoc.Core.Tests;

public static class Fixtures
{
    public static string RideSmallDb()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var p = Path.Combine(dir.FullName, "data", "ride_small.db");
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("ride_small.db not found walking up from " + AppContext.BaseDirectory);
    }

    public static SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={RideSmallDb()};Mode=ReadOnly");
        conn.Open();
        return conn;
    }
}
