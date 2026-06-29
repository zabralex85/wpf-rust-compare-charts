using System;

namespace TelemetryPoc.App.Viz;

public enum WidgetKind { Gauge, Line, Map }

public sealed record Widget(
    string Id, WidgetKind Kind, int? ChannelId,
    string Name, string Unit,
    int Col, int Row, int Cols, int Rows, int Zoom);

/// <summary>Pure grid geometry + widget sizing, ported from the Rust app's
/// widgetModel.ts + dropGrid.ts. 1-indexed columns/rows, no collision.</summary>
public static class WidgetLayout
{
    public const int Pitch = 168;
    public const int CellSize = 158;
    public const int Gap = 10;
    public const int Deadzone = 144; // Pitch - 24
    public const int ZoomMin = 1;
    public const int ZoomMax = 8;

    /// <summary>Canvas-relative point (already scroll-adjusted) → 1-indexed cell.</summary>
    public static (int Col, int Row) CellFromPoint(double x, double y)
        => (Math.Max(1, (int)Math.Floor(x / Pitch) + 1),
            Math.Max(1, (int)Math.Floor(y / Pitch) + 1));

    /// <summary>Pixel drag delta → grid-cell step with a 144px deadzone.</summary>
    public static int ResizeStep(double deltaPx)
        => deltaPx >= 0
            ? (int)Math.Floor((deltaPx + Deadzone) / Pitch)
            : (int)Math.Ceiling((deltaPx - Deadzone) / Pitch);

    public static (int Cols, int Rows) ClampSize(WidgetKind kind, int cols, int rows)
    {
        switch (kind)
        {
            case WidgetKind.Gauge:
                var s = Math.Clamp(Math.Max(cols, rows), 1, 6);
                return (s, s);
            case WidgetKind.Line:
                return (Math.Clamp(cols, 1, 6), Math.Clamp(rows, 1, 4));
            case WidgetKind.Map:
                return (Math.Clamp(cols, 2, 8), Math.Clamp(rows, 2, 6));
            default:
                return (cols, rows);
        }
    }

    public static (WidgetKind Kind, int Cols, int Rows) Toggle(WidgetKind kind, int cols, int rows)
    {
        if (kind == WidgetKind.Gauge)
        {
            var (c, r) = ClampSize(WidgetKind.Line, Math.Max(2, cols), Math.Max(1, rows));
            return (WidgetKind.Line, c, r);
        }
        if (kind == WidgetKind.Line)
        {
            var (c, r) = ClampSize(WidgetKind.Gauge, 1, 1);
            return (WidgetKind.Gauge, c, r);
        }
        return (kind, cols, rows); // map does not toggle
    }

    public static int ZoomBy(int zoom, double factor)
        => (int)Math.Clamp(Math.Round(zoom * factor, MidpointRounding.AwayFromZero), ZoomMin, ZoomMax);

    /// <summary>Row-major first-fit packing in a virtual grid `seedCols` wide.
    /// Returns the 1-indexed top-left cell for a cols×rows block that does not
    /// overlap any already-placed widget. No row cap (grid grows downward).</summary>
    public static (int Col, int Row) FirstFit(
        System.Collections.Generic.IReadOnlyList<Widget> placed, int cols, int rows, int seedCols = 8)
    {
        for (int row = 1; ; row++)
            for (int col = 1; col + cols - 1 <= seedCols; col++)
            {
                bool free = true;
                foreach (var p in placed)
                {
                    bool overlap = col < p.Col + p.Cols && p.Col < col + cols
                                && row < p.Row + p.Rows && p.Row < row + rows;
                    if (overlap) { free = false; break; }
                }
                if (free) return (col, row);
            }
    }
}
