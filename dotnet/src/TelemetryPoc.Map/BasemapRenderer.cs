using SkiaSharp;

namespace TelemetryPoc.Map;

public static class BasemapRenderer
{
    public static void Render(SKCanvas canvas, Region region, MbTilesReader reader)
    {
        canvas.Clear(SKColor.Parse(MapStyle.BackgroundHex));

        var decoded = new List<(TileRef Tile, IReadOnlyList<MapFeature> Feats)>();
        foreach (var t in TileMath.VisibleTiles(region))
        {
            var feats = reader.Decoded(t.Z, t.X, t.Y); // memoised read+gunzip+decode
            if (feats is not null) decoded.Add((t, feats));
        }

        foreach (var layer in MapStyle.Layers)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColor.Parse(layer.ColorHex).WithAlpha((byte)(layer.Opacity * 255)),
                Style = layer.Kind == PaintKind.Fill ? SKPaintStyle.Fill : SKPaintStyle.Stroke,
                StrokeWidth = (float)layer.Width,
            };
            var wantPolygon = layer.Kind == PaintKind.Fill;
            foreach (var (tile, feats) in decoded)
                foreach (var f in feats)
                {
                    if (f.SourceLayer != layer.SourceLayer) continue;
                    var isPoly = f.Type == MvtGeomType.Polygon;
                    if (wantPolygon != isPoly) continue;
                    using var path = BuildPath(tile, f);
                    canvas.DrawPath(path, paint);
                }
        }

        DrawLabels(canvas, decoded, region.Zoom);
    }

    private static SKPath BuildPath(TileRef tile, MapFeature f)
    {
        var path = new SKPath();
        foreach (var ring in f.Rings)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                var (sx, sy) = TileProject.ToScreen(tile.ScreenX, tile.ScreenY, ring[i].X, ring[i].Y, f.Extent, tile.PixSize);
                if (i == 0) path.MoveTo((float)sx, (float)sy);
                else path.LineTo((float)sx, (float)sy);
            }
            if (f.Type == MvtGeomType.Polygon) path.Close();
        }
        return path;
    }

    // Keep label density readable: major places always, smaller places only as you zoom
    // in, and road names only at street zoom. Without this every hamlet + road floods the map.
    private static bool WantLabel(MapFeature f, int zoom)
    {
        f.Props.TryGetValue("class", out var cls);
        if (f.SourceLayer == "place")
            return cls switch
            {
                "city" => true,
                "town" => zoom >= 9,
                "village" => zoom >= 12,
                "suburb" or "neighbourhood" or "quarter" => zoom >= 14,
                _ => zoom >= 13,
            };
        if (f.SourceLayer == "transportation_name")
            return zoom >= 14; // road names only at street level
        return false;
    }

    private static void DrawLabels(SKCanvas canvas, List<(TileRef Tile, IReadOnlyList<MapFeature> Feats)> decoded, int zoom)
    {
        using var fill = new SKPaint { IsAntialias = true, TextSize = 11, Color = SKColor.Parse("#aebccd") };
        using var roadFill = new SKPaint { IsAntialias = true, TextSize = 10, Color = SKColor.Parse("#8a99ad") };
        using var halo = new SKPaint { IsAntialias = true, TextSize = 11, Color = SKColor.Parse("#0a0e14"), Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f };

        var candidates = new List<(LabelBox Box, SKPaint Paint)>();
        foreach (var (tile, feats) in decoded)
            foreach (var f in feats)
            {
                if (f.SourceLayer != "place" && f.SourceLayer != "transportation_name") continue;
                if (!WantLabel(f, zoom)) continue;
                if (!f.Props.TryGetValue("name:latin", out var name) && !f.Props.TryGetValue("name", out name)) continue;
                if (string.IsNullOrWhiteSpace(name) || f.Rings.Count == 0 || f.Rings[0].Count == 0) continue;
                var pt = f.Rings[0][f.Rings[0].Count / 2]; // a representative vertex
                var (sx, sy) = TileProject.ToScreen(tile.ScreenX, tile.ScreenY, pt.X, pt.Y, f.Extent, tile.PixSize);
                var paint = f.SourceLayer == "place" ? fill : roadFill;
                var w = paint.MeasureText(name);
                candidates.Add((new LabelBox(name, sx, sy - paint.TextSize, w, paint.TextSize), paint));
            }

        var placed = LabelLayout.Place(candidates.Select(c => c.Box).ToList());
        var placedSet = new HashSet<LabelBox>(placed);
        foreach (var (box, paint) in candidates)
        {
            if (!placedSet.Contains(box)) continue;
            halo.TextSize = paint.TextSize;
            canvas.DrawText(box.Text, (float)box.X, (float)(box.Y + box.H), halo);
            canvas.DrawText(box.Text, (float)box.X, (float)(box.Y + box.H), paint);
        }
    }
}
