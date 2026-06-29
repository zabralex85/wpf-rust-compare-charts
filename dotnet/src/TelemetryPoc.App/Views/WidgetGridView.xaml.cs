using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace TelemetryPoc.App.Views;

public partial class WidgetGridView : UserControl
{
    public WidgetGridView() => InitializeComponent();

    private void OnCanvasDragOver(object sender, DragEventArgs e) { }
    private void OnCanvasDrop(object sender, DragEventArgs e) { }
    private void OnHeaderDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { }
    private void OnResizeStart(object sender, DragStartedEventArgs e) { }
    private void OnResizeDelta(object sender, DragDeltaEventArgs e) { }
    private void OnToggle(object sender, RoutedEventArgs e) { }
    private void OnRemove(object sender, RoutedEventArgs e) { }
}
