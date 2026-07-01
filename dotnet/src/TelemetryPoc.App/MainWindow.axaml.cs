using Avalonia.Controls;
using Avalonia.Threading;
using TelemetryPoc.App.ViewModels;

namespace TelemetryPoc.App;

public partial class MainWindow : Window
{
    private readonly RideSession _session;

    public MainWindow(RideSession session, TopBarViewModel topBar, OverviewViewModel overview,
        TransportViewModel transport, HudViewModel hud)
    {
        InitializeComponent();
        _session = session;
        // Region DataContexts are assigned as each view is added to MainWindow.axaml
        // in its task (e.g. TopBar.DataContext = topBar;). Keep the VM fields wired here.
        Opened += (_, _) => _session.StartAsync();
        Closed += (_, _) => _session.Dispose();
        _session.ErrorChanged += () => Dispatcher.UIThread.Post(() =>
        {
            ErrorBanner.Text = _session.Error;
            ErrorBanner.IsVisible = _session.Error is not null;
        });
    }

    // No parameterless ctor: MainWindow is DI-constructed only (Generic Host graph in
    // App.axaml.cs); this repo's shell/GUI convention is build-verify + launch-confirm,
    // not XAML-previewer-driven, so a design-time-only ctor isn't needed.
}
