using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using SkiaSharp;
using TelemetryPoc.App.ViewModels;
using TelemetryPoc.Domain;
using TelemetryPoc.Presentation;

namespace TelemetryPoc.App.Views;

public partial class MapWidgetView : UserControl
{
    private MapWidgetViewModel? _vm;
    private SKImage? _basemap;          // basemap rasterised once per region; blitted each frame
    private int _basemapW, _basemapH;
    private Region? _renderedFor;
    private Point _lastDrag;
    private bool _dragging;
    private double _panX, _panY; // live drag offset (px); applied to Region only on release

    // Smooth zoom: scale the cached basemap around the cursor instantly on each wheel
    // notch, then rebuild tiles once after the wheel settles (debounced) — no per-notch freeze.
    private int _pendingZoomSteps;
    private double _zoomScale = 1.0, _zoomFocusX, _zoomFocusY;
    private readonly DispatcherTimer _zoomDebounce;

    public MapWidgetView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => { if (_vm is null) { OnDataContextChanged(this, EventArgs.Empty); } };
        Unloaded += (_, _) => Detach();
        Skia.PointerWheelChanged += OnWheel;
        Skia.PointerPressed += OnDown;
        Skia.PointerMoved += OnMove;
        Skia.PointerReleased += OnUp;
        Skia.PaintSurface += OnPaint;
        _zoomDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _zoomDebounce.Tick += (_, _) => CommitZoom();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Detach();
        _vm = DataContext as MapWidgetViewModel;
        if (_vm is not null) { _vm.Updated += OnTick; _vm.Reset += OnReset; }
    }

    private void Detach()
    {
        if (_vm is not null) { _vm.Updated -= OnTick; _vm.Reset -= OnReset; }
        _vm = null;
        _zoomDebounce.Stop();
        _pendingZoomSteps = 0; _zoomScale = 1.0;
        _basemap?.Dispose();
        _basemap = null;
        _renderedFor = null;
    }

    private void OnTick() => Skia.InvalidateVisual();
    private void OnReset() => Skia.InvalidateVisual();

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_vm?.Region is null)
        {
            return;
        }

        var p = e.GetPosition(Skia);
        int step = e.Delta.Y > 0 ? +1 : -1;
        int cur = _vm.Region.Zoom;
        // Accumulate against the zoom range so the preview can't scale past the rebuildable level.
        int target = Math.Clamp(cur + _pendingZoomSteps + step, TileMath.MinZoom, TileMath.MaxDisplayZoom);
        _pendingZoomSteps = target - cur;
        _zoomFocusX = p.X; _zoomFocusY = p.Y;
        _zoomScale = Math.Pow(2, _pendingZoomSteps); // each integer level = 2x screen scale
        Skia.InvalidateVisual();
        _zoomDebounce.Stop(); _zoomDebounce.Start();
        e.Handled = true; // consume so the outer grid ScrollViewer doesn't scroll instead of zooming
    }

    private void CommitZoom()
    {
        _zoomDebounce.Stop();
        if (_vm?.Region is not null && _pendingZoomSteps != 0)
        {
            _vm.SetRegion(MapInteract.ZoomAt(_vm.Region, _zoomFocusX, _zoomFocusY,
                _pendingZoomSteps, TileMath.MinZoom, TileMath.MaxDisplayZoom));
        }
        _pendingZoomSteps = 0; _zoomScale = 1.0;
        Skia.InvalidateVisual();
    }

    private void OnDown(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Skia).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // ---- original OnDown body ----
        if (_vm?.Region is not null)
        {
            if (_pendingZoomSteps != 0)
            {
                CommitZoom(); // settle any in-flight zoom before panning
            }

            _dragging = true; _lastDrag = e.GetPosition(Skia); _panX = 0; _panY = 0; e.Pointer.Capture(Skia);
        }

        // ---- original OnMaybeDoubleClick body (merged: fires after OnDown, same as WPF's
        // second MouseLeftButtonDown subscriber) ----
        if (e.ClickCount < 2 || _vm is null)
        {
            return;
        }

        _dragging = false; // OnDown already fired for this click; cancel the pan it started
        // re-fit to the whole-ride GPS bounds (the original FitBbox view)
        var b = _vm.GpsBoundsForRefit();
        if (b is null || _vm.Region is null)
        {
            return;
        }

        var (cLat, cLon, z) = TileMath.FitBbox(b.Value.MinLat, b.Value.MinLon, b.Value.MaxLat, b.Value.MaxLon,
                                               _vm.Region.Width, _vm.Region.Height);
        _vm.SetRegion(_vm.Region with { CenterLat = cLat, CenterLon = cLon, Zoom = z });
        Skia.InvalidateVisual();
    }

    private void OnMove(object? sender, PointerEventArgs e)
    {
        if (!_dragging || _vm?.Region is null)
        {
            return;
        }

        var p = e.GetPosition(Skia);
        _panX += p.X - _lastDrag.X; _panY += p.Y - _lastDrag.Y;
        _lastDrag = p;
        // Translate the cached basemap during drag (cheap); rebuild once on release.
        Skia.InvalidateVisual();
    }

    private void OnUp(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging && _vm?.Region is not null && (_panX != 0 || _panY != 0))
        {
            _vm.SetRegion(MapInteract.Pan(_vm.Region, _panX, _panY));
        }

        _dragging = false; _panX = 0; _panY = 0;
        e.Pointer.Capture(null);
        Skia.InvalidateVisual();
    }

    private void OnPaint(SKCanvas canvas, int w, int h)
    {
        canvas.Clear(SKColor.Parse(MapStyle.BackgroundHex));
        if (_vm is null)
        {
            return;
        }

        _vm.EnsureRegion(w, h);
        if (_vm.Region is null)
        {
            return;
        }

        if (_basemap is null || _basemapW != w || _basemapH != h || !_vm.Region.Equals(_renderedFor))
        {
            _basemap?.Dispose();
            BuildBasemap(_vm.Region, w, h);
            _renderedFor = _vm.Region;
            _basemapW = w; _basemapH = h;
        }

        // Preview pan (translate) and zoom (scale-around-cursor) on the cached basemap
        // instead of rebuilding tiles every event; the real region+rebuild happens on
        // release (pan) / debounce (zoom).
        bool shifted = _dragging && (_panX != 0 || _panY != 0);
        bool zooming = _pendingZoomSteps != 0;
        if (shifted || zooming)
        {
            canvas.Save();
            if (shifted)
            {
                canvas.Translate((float)_panX, (float)_panY);
            }

            if (zooming)
            {
                canvas.Scale((float)_zoomScale, (float)_zoomScale, (float)_zoomFocusX, (float)_zoomFocusY);
            }
        }

        if (_basemap is not null)
        {
            canvas.DrawImage(_basemap, 0, 0, new SKSamplingOptions(SKFilterMode.Linear)); // fast blit, no vector re-raster
        }

        var (lat, lon) = _vm.Track;
        TrackOverlay.Draw(canvas, _vm.Region, lat, lon);

        if (shifted || zooming)
        {
            canvas.Restore();
        }
    }

    private void BuildBasemap(Region region, int w, int h)
    {
        if (_vm is null || w < 1 || h < 1) { _basemap = null; return; }
        try
        {
            // Rasterise the vector basemap once into an image. Per-frame paint then blits
            // this image (cheap) instead of replaying every path via DrawPicture (the ~80%
            // CPU sink) — the tile set only changes on pan-release / zoom-commit / resize.
            using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            BasemapRenderer.Render(surface.Canvas, region, _vm.Tiles);
            _basemap = surface.Snapshot();
        }
        catch { _basemap = null; }
    }
}
