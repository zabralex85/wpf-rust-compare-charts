namespace TelemetryPoc.Map;

public enum PaintKind { Fill, Line }

public sealed record StyleLayer(string Id, string SourceLayer, PaintKind Kind, string ColorHex, double Width, double Opacity);

public static class MapStyle
{
    public const string BackgroundHex = "#0a0e14";

    public static IReadOnlyList<StyleLayer> Layers { get; } = new[]
    {
        new StyleLayer("water", "water", PaintKind.Fill, "#16384f", 0, 1.0),
        new StyleLayer("landcover", "landcover", PaintKind.Fill, "#0c1118", 0, 0.8),
        new StyleLayer("landuse", "landuse", PaintKind.Fill, "#111820", 0, 0.6),
        new StyleLayer("transportation-casing", "transportation", PaintKind.Line, "#0a0e14", 3.0, 1.0),
        new StyleLayer("transportation", "transportation", PaintKind.Line, "#5b6470", 1.5, 1.0),
        new StyleLayer("building", "building", PaintKind.Fill, "#232d38", 0, 0.8),
    };
}
