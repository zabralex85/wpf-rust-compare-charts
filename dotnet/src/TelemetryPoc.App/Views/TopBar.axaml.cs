using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace TelemetryPoc.App.Views;

public partial class TopBar : UserControl
{
    public TopBar()
    {
        InitializeComponent();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.Source is Visual visual && visual.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        (VisualRoot as Window)?.BeginMoveDrag(e);
    }

    private void OnMinimize(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is Window w)
        {
            w.WindowState = WindowState.Minimized;
        }
    }

    private void OnMaximize(object? sender, RoutedEventArgs e)
    {
        if (VisualRoot is Window w)
        {
            w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => (VisualRoot as Window)?.Close();
}
