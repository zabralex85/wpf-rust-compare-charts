using System.Windows;
using System.Windows.Controls;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using TelemetryPoc.App.ViewModels;
using TelemetryPoc.Map;

namespace TelemetryPoc.App.Views;

public partial class MapWidgetView : UserControl
{
    private MapWidgetViewModel? _vm;
    private SKPicture? _basemap;

    public MapWidgetView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => { if (_vm is null) OnDataContextChanged(this, default); };
        Unloaded += (_, _) => Detach();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();
        _vm = DataContext as MapWidgetViewModel;
        if (_vm is not null) _vm.Updated += OnTick;
    }

    private void Detach()
    {
        if (_vm is not null) _vm.Updated -= OnTick;
        _vm = null; // so the Loaded re-subscribe guard (if _vm is null) fires on tree re-entry
        _basemap?.Dispose();
        _basemap = null;
    }

    private void OnTick() => Skia.InvalidateVisual();

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColor.Parse(MapStyle.BackgroundHex));
        if (_vm is null) return;

        var w = e.Info.Width;
        var h = e.Info.Height;
        _vm.EnsureRegion(w, h);
        if (_vm.Region is null) return;

        if (_basemap is null) BuildBasemap(_vm.Region, w, h);
        if (_basemap is not null) canvas.DrawPicture(_basemap);

        var (lat, lon) = _vm.Track;
        TrackOverlay.Draw(canvas, _vm.Region, lat, lon);
    }

    private void BuildBasemap(Region region, int w, int h)
    {
        if (string.IsNullOrEmpty(_vm?.MbTilesPath)) return;
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
