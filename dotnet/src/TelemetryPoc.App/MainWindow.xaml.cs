using System.Windows;
using System.Windows.Data;

namespace TelemetryPoc.App;

public partial class MainWindow : Window
{
    private readonly RideSession _session = new();

    public MainWindow()
    {
        InitializeComponent();
        TopBar.ClockText.SetBinding(System.Windows.Controls.TextBlock.TextProperty,
            new Binding(nameof(RideSession.ClockText)) { Source = _session });
        Loaded += (_, _) => _session.Start();
    }
}
