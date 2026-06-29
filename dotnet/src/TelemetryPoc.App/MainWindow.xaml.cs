using System.Windows;
using TelemetryPoc.App.ViewModels;

namespace TelemetryPoc.App;

public partial class MainWindow : Window
{
    private readonly RideSession _session = new();

    public MainWindow()
    {
        InitializeComponent();
        TopBar.DataContext = new TopBarViewModel(_session);
        Overview.DataContext = new OverviewViewModel(_session);
        Transport.DataContext = new TransportViewModel(_session);
        Hud.DataContext = new HudViewModel(_session);
        Loaded += (_, _) => _session.Start();
    }
}
