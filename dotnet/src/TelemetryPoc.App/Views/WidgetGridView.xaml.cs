using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace TelemetryPoc.App.Views;

public partial class WidgetGridView : UserControl
{
    public WidgetGridView() => InitializeComponent();

    private void OnCanvasDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("inu-channel") ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnCanvasDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not ViewModels.DashboardViewModel dvm) return;
        if (!e.Data.GetDataPresent("inu-channel")) return;
        var channelId = (int)e.Data.GetData("inu-channel");
        var canvas = FindCanvas();
        if (canvas is null) return;
        var p = e.GetPosition(canvas);
        var (col, row) = TelemetryPoc.App.Viz.WidgetLayout.CellFromPoint(p.X, p.Y);
        dvm.AddGauge(channelId, col, row);
    }

    private ViewModels.WidgetViewModel? _moving;

    private void OnHeaderDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_moving is not null) return; // ignore a second grab while a move is active (no leaked handlers)
        if (sender is not FrameworkElement fe || fe.DataContext is not ViewModels.WidgetViewModel w) return;
        _moving = w;
        fe.CaptureMouse();
        fe.MouseMove += OnHeaderMove;
        fe.MouseLeftButtonUp += OnHeaderUp;
        e.Handled = true;
    }

    private void OnHeaderMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_moving is null) return;
        var canvas = FindCanvas();
        if (canvas is null) return;
        var p = e.GetPosition(canvas);
        var (col, row) = TelemetryPoc.App.Viz.WidgetLayout.CellFromPoint(p.X, p.Y);
        if (DataContext is ViewModels.DashboardViewModel dvm) dvm.Move(_moving.Id, col, row);
    }

    private void OnHeaderUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            fe.ReleaseMouseCapture();
            fe.MouseMove -= OnHeaderMove;
            fe.MouseLeftButtonUp -= OnHeaderUp;
        }
        _moving = null;
    }

    private int _resizeStartCols, _resizeStartRows;
    private double _accumX, _accumY;

    private void OnResizeStart(object sender, DragStartedEventArgs e)
    {
        if (Widget(sender) is not { } w) return;
        _resizeStartCols = w.Cols; _resizeStartRows = w.Rows;
        _accumX = 0; _accumY = 0;
    }

    private void OnResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (Widget(sender) is not { } w || DataContext is not ViewModels.DashboardViewModel dvm) return;
        _accumX += e.HorizontalChange;
        _accumY += e.VerticalChange;
        var cols = _resizeStartCols + TelemetryPoc.App.Viz.WidgetLayout.ResizeStep(_accumX);
        var rows = _resizeStartRows + TelemetryPoc.App.Viz.WidgetLayout.ResizeStep(_accumY);
        dvm.Resize(w.Id, cols, rows);
    }

    private void OnToggle(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.DashboardViewModel dvm && Widget(sender) is { } w) dvm.Toggle(w.Id);
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.DashboardViewModel dvm && Widget(sender) is { } w) dvm.Remove(w.Id);
    }

    private static ViewModels.WidgetViewModel? Widget(object sender)
        => (sender as FrameworkElement)?.DataContext as ViewModels.WidgetViewModel;

    private void OnZoomIn(object sender, RoutedEventArgs e) => Zoom(sender, 2.0);
    private void OnZoomOut(object sender, RoutedEventArgs e) => Zoom(sender, 0.5);
    private void OnZoomReset(object sender, RoutedEventArgs e)
    {
        if (MenuWidget(sender) is { Content: ViewModels.LineChartViewModel l }) { l.ResetZoom(); }
    }

    private void Zoom(object sender, double factor)
    {
        if (DataContext is ViewModels.DashboardViewModel dvm && MenuWidget(sender) is { } w) dvm.ZoomBy(w.Id, factor);
    }

    private static ViewModels.WidgetViewModel? MenuWidget(object sender)
    {
        // MenuItem.DataContext is the WidgetViewModel (inherited through the ContextMenu's PlacementTarget).
        var mi = sender as FrameworkElement;
        if (mi?.DataContext is ViewModels.WidgetViewModel w) return w;
        return null;
    }

    private Canvas? FindCanvas()
    {
        // The ItemsControl's ItemsPanel Canvas (x:Name="GridCanvas"). Match by name so a
        // theme/template that inserts its own Canvas before ours can't be picked by mistake.
        return FindNamedChild<Canvas>(this, "GridCanvas") ?? FindVisualChild<Canvas>(this);
    }

    private static T? FindNamedChild<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (c is T t && t.Name == name) return t;
            var r = FindNamedChild<T>(c, name);
            if (r is not null) return r;
        }
        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (c is T t) return t;
            var r = FindVisualChild<T>(c);
            if (r is not null) return r;
        }
        return null;
    }
}
