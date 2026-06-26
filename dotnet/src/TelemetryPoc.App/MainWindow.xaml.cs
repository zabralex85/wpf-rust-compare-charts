using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.DependencyInjection;

namespace TelemetryPoc.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
        // (replay/store services registered in Task 8)
        blazorWebView.Services = services.BuildServiceProvider();
        blazorWebView.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(Main) });
    }
}
