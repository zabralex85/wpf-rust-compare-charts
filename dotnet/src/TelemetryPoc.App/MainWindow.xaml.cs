using System.Windows;
using System.Windows.Data;
using TelemetryPoc.App.ViewModels;

namespace TelemetryPoc.App;

public partial class MainWindow : Window
{
    private readonly RideSession _session = new();

    public MainWindow()
    {
        InitializeComponent();
        TopBar.ClockText.SetBinding(System.Windows.Controls.TextBlock.TextProperty,
            new Binding(nameof(RideSession.ClockText)) { Source = _session });
        Overview.DataContext = new OverviewViewModel(_session);
        Loaded += (_, _) => _session.Start();
    }
}
