namespace TelemetryPoc.Domain;

public enum MvtGeomType { Point, Line, Polygon, Unknown }

public sealed record MapFeature(
    string SourceLayer,
    MvtGeomType Type,
    IReadOnlyList<IReadOnlyList<(long X, long Y)>> Rings,
    IReadOnlyDictionary<string, string> Props,
    int Extent);
