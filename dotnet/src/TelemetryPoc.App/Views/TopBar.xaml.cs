using System.Windows;
using System.Windows.Controls;

namespace TelemetryPoc.App.Views;

public partial class TopBar : UserControl
{
    public TopBar()
    {
        InitializeComponent();
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        var w = Window.GetWindow(this);
        if (w is not null)
        {
            w.WindowState = WindowState.Minimized;
        }
    }

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        var w = Window.GetWindow(this);
        if (w is not null)
        {
            w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Window.GetWindow(this)?.Close();
}
