using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace TelemetryPoc.App.Controls;

/// <summary>
/// Avalonia replacement for SkiaSharp.Views.WPF.SKElement: leases the underlying Skia
/// canvas during the render pass and raises <see cref="PaintSurface"/> so callers can
/// draw with SkiaSharp directly. Pixel size is passed so callers size their raster to
/// the device surface.
/// </summary>
public sealed class SkiaCanvasControl : Control
{
    public event Action<SKCanvas, int, int>? PaintSurface;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        if (bounds.Width < 1 || bounds.Height < 1) { return; }
        context.Custom(new SkiaDrawOp(new Rect(bounds.Size), this));
    }

    private sealed class SkiaDrawOp : ICustomDrawOperation
    {
        private readonly SkiaCanvasControl _owner;
        public SkiaDrawOp(Rect bounds, SkiaCanvasControl owner) { Bounds = bounds; _owner = owner; }

        public Rect Bounds { get; }
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (lease is null) { return; }   // non-Skia backend (should not happen on desktop)
            using var api = lease.Lease();
            var canvas = api.SkCanvas;
            int w = (int)Math.Ceiling(Bounds.Width);
            int h = (int)Math.Ceiling(Bounds.Height);
            _owner.PaintSurface?.Invoke(canvas, w, h);
        }
    }
}
