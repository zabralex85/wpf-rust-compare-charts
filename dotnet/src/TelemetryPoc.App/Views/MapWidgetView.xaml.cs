using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using TelemetryPoc.App.ViewModels;
using TelemetryPoc.Map;

namespace TelemetryPoc.App.Views;

public partial class MapWidgetView : UserControl
{
    private MapWidgetViewModel? _vm;
    private SKPicture? _basemap;
    private Region? _renderedFor;
    private Point _lastDrag;
    private bool _dragging;

    public MapWidgetView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => { if (_vm is null) OnDataContextChanged(this, default); };
        Unloaded += (_, _) => Detach();
        Skia.MouseWheel += OnWheel;
        Skia.MouseLeftButtonDown += OnDown;
        Skia.MouseMove += OnMove;
        Skia.MouseLeftButtonUp += OnUp;
        Skia.MouseLeftButtonDown += OnMaybeDoubleClick;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();
        _vm = DataContext as MapWidgetViewModel;
        if (_vm is not null) { _vm.Updated += OnTick; _vm.Reset += OnReset; }
    }

    private void Detach()
    {
        if (_vm is not null) { _vm.Updated -= OnTick; _vm.Reset -= OnReset; }
        _vm = null;
        _basemap?.Dispose();
        _basemap = null;
        _renderedFor = null;
    }

    private void OnTick() => Skia.InvalidateVisual();
    private void OnReset() => Skia.InvalidateVisual();

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (_vm?.Region is null) return;
        var p = e.GetPosition(Skia);
        int step = e.Delta > 0 ? +1 : -1;
        _vm.SetRegion(MapInteract.ZoomAt(_vm.Region, p.X, p.Y, step, TileMath.MinZoom, TileMath.MaxZoom));
        Skia.InvalidateVisual();
        e.Handled = true; // consume so the outer grid ScrollViewer doesn't scroll instead of zooming
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm?.Region is null) return;
        _dragging = true; _lastDrag = e.GetPosition(Skia); Skia.CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _vm?.Region is null) return;
        var p = e.GetPosition(Skia);
        var dx = p.X - _lastDrag.X; var dy = p.Y - _lastDrag.Y;
        _lastDrag = p;
        _vm.SetRegion(MapInteract.Pan(_vm.Region, dx, dy));
        Skia.InvalidateVisual();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false; Skia.ReleaseMouseCapture();
    }

    private void OnMaybeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2 || _vm is null) return;
        _dragging = false; // OnDown already fired for this click; cancel the pan it started
        // re-fit to the whole-ride GPS bounds (the original FitBbox view)
        var b = _vm.GpsBoundsForRefit();
        if (b is null || _vm.Region is null) return;
        var (cLat, cLon, z) = TileMath.FitBbox(b.Value.MinLat, b.Value.MinLon, b.Value.MaxLat, b.Value.MaxLon,
                                               _vm.Region.Width, _vm.Region.Height);
        _vm.SetRegion(_vm.Region with { CenterLat = cLat, CenterLon = cLon, Zoom = z });
        Skia.InvalidateVisual();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColor.Parse(MapStyle.BackgroundHex));
        if (_vm is null) return;

        var w = e.Info.Width;
        var h = e.Info.Height;
        _vm.EnsureRegion(w, h);
        if (_vm.Region is null) return;

        if (_basemap is null || !_vm.Region.Equals(_renderedFor))
        {
            _basemap?.Dispose();
            BuildBasemap(_vm.Region, w, h);
            _renderedFor = _vm.Region;
        }
        if (_basemap is not null) canvas.DrawPicture(_basemap);

        var (lat, lon) = _vm.Track;
        TrackOverlay.Draw(canvas, _vm.Region, lat, lon);
    }

    private void BuildBasemap(Region region, int w, int h)
    {
        if (string.IsNullOrEmpty(_vm?.MbTilesPath)) { _basemap = null; return; }
        try
        {
            using var reader = new MbTilesReader(_vm.MbTilesPath);
            using var rec = new SKPictureRecorder();
            var rc = rec.BeginRecording(new SKRect(0, 0, w, h));
            BasemapRenderer.Render(rc, region, reader);
            _basemap = rec.EndRecording();
        }
        catch { _basemap = null; }
    }
}
