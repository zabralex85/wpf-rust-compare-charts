using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;

namespace TelemetryPoc.App.Controls;

/// <summary>
/// Per-frame tick for the FPS / frame-time HUD — a drop-in for WPF's
/// CompositionTarget.Rendering. It drives off the compositor's frame updates
/// (<see cref="Compositor.RequestCompositionUpdate"/>) rather than overriding
/// <c>Render()</c>: a HUD-sized helper control is not reliably repainted every frame on
/// every backend (the Linux/X11 renderer culls it, so <c>Render()</c> never fires there
/// and the HUD stayed blank). The compositor callback ticks once per composited frame
/// regardless of the control's size or paint state.
/// </summary>
public sealed class FrameClock : Control
{
    public event EventHandler<TimeSpan>? Rendering;
    private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
    private Compositor? _compositor;
    private bool _attached;

    public FrameClock()
    {
        IsHitTestVisible = false;
        Width = 0; Height = 0;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _compositor = ElementComposition.GetElementVisual(this)?.Compositor;
        RequestFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _compositor = null;
    }

    private void RequestFrame()
    {
        if (_attached)
        {
            _compositor?.RequestCompositionUpdate(OnFrame);
        }
    }

    private void OnFrame()
    {
        Rendering?.Invoke(this, _sw.Elapsed);
        RequestFrame(); // re-arm for the next composited frame
    }
}
