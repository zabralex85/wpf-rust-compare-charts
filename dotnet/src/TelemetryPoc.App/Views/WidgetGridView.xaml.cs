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

    private void OnHeaderDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { }
    private void OnResizeStart(object sender, DragStartedEventArgs e) { }
    private void OnResizeDelta(object sender, DragDeltaEventArgs e) { }

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
