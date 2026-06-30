namespace TelemetryPoc.Domain;

public sealed record Region(double CenterLat, double CenterLon, int Zoom, double Width, double Height);
