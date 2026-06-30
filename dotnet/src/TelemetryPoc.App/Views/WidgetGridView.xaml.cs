using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TelemetryPoc.App.ViewModels;
using TelemetryPoc.Presentation;

namespace TelemetryPoc.App.Views;

public partial class WidgetGridView : UserControl
{
    public WidgetGridView()
    {
        InitializeComponent();
    }

    private void OnCanvasDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("inu-channel") ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnCanvasDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not DashboardViewModel dvm)
        {
            return;
        }

        if (!e.Data.GetDataPresent("inu-channel"))
        {
            return;
        }

        var channelId = (int)e.Data.GetData("inu-channel");
        var canvas = FindCanvas();
        if (canvas is null)
        {
            return;
        }

        var p = e.GetPosition(canvas);
        var (col, row) = WidgetLayout.CellFromPoint(p.X, p.Y);
        dvm.AddGauge(channelId, col, row);
    }

    private WidgetViewModel? _moving;
    private int _moveCol, _moveRow;

    private void OnHeaderDown(object sender, MouseButtonEventArgs e)
    {
        if (_moving is not null)
        {
            return; // ignore a second grab while a move is active (no leaked handlers)
        }

        if (sender is not FrameworkElement fe || fe.DataContext is not WidgetViewModel w)
        {
            return;
        }

        _moving = w;
        _moveCol = w.Col; _moveRow = w.Row;
        fe.CaptureMouse();
        fe.MouseMove += OnHeaderMove;
        fe.MouseLeftButtonUp += OnHeaderUp;
        ShowGhost(w.Col, w.Row, w.Width, w.Height); // placeholder marks the landing cell while dragging
        e.Handled = true;
    }

    private void OnHeaderMove(object sender, MouseEventArgs e)
    {
        if (_moving is null)
        {
            return;
        }

        var canvas = FindCanvas();
        if (canvas is null)
        {
            return;
        }

        var p = e.GetPosition(canvas);
        (_moveCol, _moveRow) = WidgetLayout.CellFromPoint(p.X, p.Y);
        // Don't move the widget live — show a ghost at the target; commit on release.
        ShowGhost(_moveCol, _moveRow, _moving.Width, _moving.Height);
    }

    private void OnHeaderUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            fe.ReleaseMouseCapture();
            fe.MouseMove -= OnHeaderMove;
            fe.MouseLeftButtonUp -= OnHeaderUp;
        }
        if (_moving is not null && DataContext is DashboardViewModel dvm)
        {
            dvm.Move(_moving.Id, _moveCol, _moveRow);
        }

        DropGhost.Visibility = Visibility.Collapsed;
        _moving = null;
    }

    private void ShowGhost(int col, int row, double width, double height)
    {
        const int pitch = WidgetLayout.Pitch;
        Canvas.SetLeft(DropGhost, (col - 1) * pitch);
        Canvas.SetTop(DropGhost, (row - 1) * pitch);
        DropGhost.Width = width;
        DropGhost.Height = height;
        DropGhost.Visibility = Visibility.Visible;
    }

    private void ShowGhostCells(int col, int row, int cols, int rows)
    {
        const int pitch = WidgetLayout.Pitch;
        const int gap = WidgetLayout.Gap;
        ShowGhost(col, row, cols * pitch - gap, rows * pitch - gap);
    }

    private WidgetViewModel? _resizing;
    private int _resizeStartCols, _resizeStartRows, _resizeCols, _resizeRows;
    private double _accumX, _accumY;

    private void OnResizeStart(object sender, DragStartedEventArgs e)
    {
        if (Widget(sender) is not { } w)
        {
            return;
        }

        _resizing = w;
        _resizeStartCols = w.Cols; _resizeStartRows = w.Rows;
        _resizeCols = w.Cols; _resizeRows = w.Rows;
        _accumX = 0; _accumY = 0;
        ShowGhostCells(w.Col, w.Row, _resizeCols, _resizeRows);
    }

    private void OnResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (_resizing is not { } w)
        {
            return;
        }
        // The Thumb is never repositioned, so HorizontalChange/VerticalChange are
        // cumulative from drag-start — assign, don't accumulate (that over-counted).
        _accumX = e.HorizontalChange;
        _accumY = e.VerticalChange;

        const int pitch = WidgetLayout.Pitch;
        const int gap = WidgetLayout.Gap;
        // Snapped cell size to commit on release.
        var cols = _resizeStartCols + WidgetLayout.ResizeStep(_accumX);
        var rows = _resizeStartRows + WidgetLayout.ResizeStep(_accumY);
        (_resizeCols, _resizeRows) = WidgetLayout.ClampSize(w.Kind, cols, rows);

        // Ghost follows the cursor 1:1 (raw pixels), clamped to the kind's min/max,
        // so the preview tracks the mouse like a normal window resize; the actual
        // widget snaps to whole cells only on release (one re-render, no blink).
        var (minC, minR) = WidgetLayout.ClampSize(w.Kind, 0, 0);
        var (maxC, maxR) = WidgetLayout.ClampSize(w.Kind, 999, 999);
        double rawW = Math.Clamp((_resizeStartCols * pitch - gap) + _accumX, minC * pitch - gap, maxC * pitch - gap);
        double rawH = Math.Clamp((_resizeStartRows * pitch - gap) + _accumY, minR * pitch - gap, maxR * pitch - gap);
        ShowGhost(w.Col, w.Row, rawW, rawH);
    }

    private void OnResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_resizing is { } w && DataContext is DashboardViewModel dvm)
        {
            dvm.Resize(w.Id, _resizeCols, _resizeRows);
        }

        DropGhost.Visibility = Visibility.Collapsed;
        _resizing = null;
    }

    private void OnToggle(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel dvm && Widget(sender) is { } w)
        {
            dvm.Toggle(w.Id);
        }
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel dvm && Widget(sender) is { } w)
        {
            dvm.Remove(w.Id);
        }
    }

    private static WidgetViewModel? Widget(object sender)
        => (sender as FrameworkElement)?.DataContext as WidgetViewModel;

    private void OnZoomIn(object sender, RoutedEventArgs e) => Zoom(sender, 2.0);
    private void OnZoomOut(object sender, RoutedEventArgs e) => Zoom(sender, 0.5);
    private void OnZoomReset(object sender, RoutedEventArgs e)
    {
        if (MenuWidget(sender) is { Content: LineChartViewModel l }) { l.ResetZoom(); }
    }

    private void Zoom(object sender, double factor)
    {
        if (DataContext is DashboardViewModel dvm && MenuWidget(sender) is { } w)
        {
            dvm.ZoomBy(w.Id, factor);
        }
    }

    private static WidgetViewModel? MenuWidget(object sender)
    {
        // MenuItem.DataContext is the WidgetViewModel (inherited through the ContextMenu's PlacementTarget).
        var mi = sender as FrameworkElement;
        if (mi?.DataContext is WidgetViewModel w)
        {
            return w;
        }

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
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t && t.Name == name)
            {
                return t;
            }

            var r = FindNamedChild<T>(c, name);
            if (r is not null)
            {
                return r;
            }
        }
        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t)
            {
                return t;
            }

            var r = FindVisualChild<T>(c);
            if (r is not null)
            {
                return r;
            }
        }
        return null;
    }
}
