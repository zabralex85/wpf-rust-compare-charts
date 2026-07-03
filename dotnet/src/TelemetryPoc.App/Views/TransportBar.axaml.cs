using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TelemetryPoc.App.ViewModels;

namespace TelemetryPoc.App.Views;

public partial class TransportBar : UserControl
{
    public TransportBar()
    {
        InitializeComponent();
    }

    private void OnPlayPause(object? sender, RoutedEventArgs e)
        => (DataContext as TransportViewModel)?.TogglePlayPause();

    private void OnSeekClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control track || track.Bounds.Width < 1)
        {
            return;
        }

        if (!e.GetCurrentPoint(track).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (DataContext is not TransportViewModel vm)
        {
            return;
        }

        var x = e.GetPosition(track).X;
        vm.Seek(Math.Clamp(x / track.Bounds.Width, 0, 1));
    }
}
