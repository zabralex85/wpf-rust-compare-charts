namespace TelemetryPoc.Domain;

public static class Severity
{
    public static string Hex(string? severity) => severity switch
    {
        "critical" => "#FFFF4D52",
        "ok" => "#FF2FD17A",
        _ => "#FF566273",
    };
}
