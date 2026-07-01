using Avalonia.Controls;
using TelemetryPoc.App.ViewModels;

namespace TelemetryPoc.App.Views;

public partial class Hud : UserControl
{
    public Hud()
    {
        InitializeComponent();
        Clock.Rendering += (_, elapsed) => (DataContext as HudViewModel)?.Tick(elapsed);
    }
}
