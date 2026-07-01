using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace TelemetryPoc.App.Controls;

/// <summary>
/// Drop-in replacement for WPF's CompositionTarget.Rendering: an invisible control
/// that re-invalidates itself every frame, raising <see cref="Rendering"/> once per
/// composited frame (~display refresh). Used by the HUD to measure real render FPS.
/// </summary>
public sealed class FrameClock : Control
{
    public event EventHandler<TimeSpan>? Rendering;
    private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
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
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rendering?.Invoke(this, _sw.Elapsed);
        // Schedule the next frame. Background priority yields to input/layout so this
        // ticks at the compositor's pace instead of spinning the CPU.
        if (_attached)
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background);
    }
}
