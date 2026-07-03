using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace TelemetryPoc.App.Controls;

/// <summary>
/// Per-frame tick for the FPS / frame-time HUD — a drop-in for WPF's
/// CompositionTarget.Rendering. It drives off the compositor's frame updates
/// (<see cref="Compositor.RequestCompositionUpdate"/>) rather than overriding
/// <c>Render()</c>: a HUD-sized helper control is not reliably repainted every frame on
/// every backend (the Linux/X11 renderer culls it, so <c>Render()</c> never fires there
/// and the HUD stayed blank). The compositor callback ticks once per composited frame
/// regardless of the control's size or paint state.
///
/// <para><b>Frame pacing.</b> Re-arming the compositor update every frame makes the whole
/// window recomposite as fast as the backend allows — 60 fps (vsync) on a GPU, unbounded on
/// software render (VirtualBox llvmpipe hit 60–70% CPU). The dashboard's data only changes at
/// 10 Hz, so compositing faster is wasted work. <c>RIDE_FPS_CAP</c> paces the loop: a
/// <see cref="DispatcherTimer"/> at the target Hz requests one composition update per tick,
/// so CPU scales with the cap. Default uncapped (free-run) — on a GPU the composite is
/// vsync-paced and cheap, and it keeps the max-FPS benchmark honest against the Rust app. Set
/// <c>RIDE_FPS_CAP=30</c> on a software renderer (VirtualBox llvmpipe ran 60–70% uncapped) to
/// throttle the expensive CPU composites.</para>
/// </summary>
public sealed class FrameClock : Control
{
    /// <summary>Frame-loop cap in fps: 0 = uncapped free-run, &gt;0 = ceiling. Sourced from
    /// <c>RideOptions.FpsCap</c> (appsettings "Ride:FpsCap" / env RIDE_FPS_CAP) and set by the
    /// host before the window is built. Static because the control is XAML-constructed (no DI).</summary>
    public static int CapHz { get; set; }

    public event EventHandler<TimeSpan>? Rendering;
    private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
    private Compositor? _compositor;
    private bool _attached;
    private readonly int _capHz;
    private DispatcherTimer? _pacer;

    public FrameClock()
    {
        IsHitTestVisible = false;
        Width = 0; Height = 0;
        _capHz = CapHz > 0 ? CapHz : 0;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _compositor = ElementComposition.GetElementVisual(this)?.Compositor;

        if (_capHz > 0)
        {
            // Paced: one composition update per timer tick, so the window composites at
            // ~_capHz fps instead of as fast as the backend can go.
            _pacer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(1000.0 / _capHz), DispatcherPriority.Render, (_, _) => Tick());
            _pacer.Start();
        }
        else
        {
            RequestFrame(); // uncapped free-run
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _compositor = null;
        _pacer?.Stop();
        _pacer = null;
    }

    // Paced path: measure the frame and ask for exactly one composite; the timer sets the rate.
    private void Tick()
    {
        Rendering?.Invoke(this, _sw.Elapsed);
        if (_attached)
        {
            _compositor?.RequestCompositionUpdate(static () => { });
        }
    }

    private void RequestFrame()
    {
        if (_attached)
        {
            _compositor?.RequestCompositionUpdate(OnFrame);
        }
    }

    // Uncapped path: re-arm every composite → free-running max-FPS loop.
    private void OnFrame()
    {
        Rendering?.Invoke(this, _sw.Elapsed);
        RequestFrame(); // re-arm for the next composited frame
    }
}
