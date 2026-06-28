namespace TelemetryPoc.App.Viz;

public static class RidePaths
{
    public static string Resolve(string? env, string baseDir, Func<string, bool> exists)
    {
        if (!string.IsNullOrWhiteSpace(env) && exists(env)) return env;
        var dir = new DirectoryInfo(baseDir);
        while (dir is not null)
        {
            foreach (var name in new[] { "ride.db", "ride_small.db" })
            {
                var p = Path.Combine(dir.FullName, "data", name);
                if (exists(p)) return p;
            }
            dir = dir.Parent;
        }
        return env ?? Path.Combine("data", "ride.db");
    }
}
