using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TelemetryPoc.App.ViewModels;

namespace TelemetryPoc.App.Views;

public partial class TransportBar : UserControl
{
    public TransportBar() => InitializeComponent();

    private void OnPlayPause(object sender, RoutedEventArgs e)
        => (DataContext as TransportViewModel)?.TogglePlayPause();

    private void OnSeekClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement track || track.ActualWidth < 1) return;
        if (DataContext is not TransportViewModel vm) return;
        var x = e.GetPosition(track).X;
        vm.Seek(System.Math.Clamp(x / track.ActualWidth, 0, 1));
    }
}
