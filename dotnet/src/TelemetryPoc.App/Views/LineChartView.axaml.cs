using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.TickGenerators;
using TelemetryPoc.App.ViewModels;
using TelemetryPoc.Presentation;

namespace TelemetryPoc.App.Views;

public partial class LineChartView : UserControl
{
    private LineChartViewModel? _vm;
    private DataLogger? _logger;
    private double _lastX = double.NegativeInfinity;

    public LineChartView()
    {
        InitializeComponent();
        StylePlot();
        // Repaint synchronously on each resize step so the plot doesn't flash an
        // empty frame while its Skia surface regenerates at the new size.
        SizeChanged += (_, _) => Plot.Refresh();
        // Right-click zoom menu. Tunnel ONLY (not also Bubble, or the handler fires twice
        // per release and opens two overlapping menus): the tunnel pass reaches this parent
        // before the AvaPlot child, so we open the menu and mark it handled before the plot
        // can swallow the right-click.
        AddHandler(PointerReleasedEvent, OnChartRightClick,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        DataContextChanged += OnDataContextChanged;
        // Re-subscribe when the view re-enters the visual tree with an already-set
        // DataContext (tab-switch / virtualization fires Loaded but not DataContextChanged).
        Loaded += (_, _) => { if (_vm is null) { OnDataContextChanged(this, EventArgs.Empty); } };
        Unloaded += (_, _) => Detach();
    }

    private void StylePlot()
    {
        var p = Plot.Plot;
        p.FigureBackground.Color = Color.FromHex("#10151d");
        p.DataBackground.Color = Color.FromHex("#0c121a");
        p.Axes.Color(Color.FromHex("#566273"));
        p.Grid.MajorLineColor = Color.FromHex("#1d2632");
        _logger = p.Add.DataLogger();
        _logger.Color = Color.FromHex("#38c5e0");
        _logger.LineWidth = 1.5f;
        _logger.ManageAxisLimits = false;
        // relative m:ss x-axis labels
        if (p.Axes.Bottom.TickGenerator is NumericAutomatic gen)
        {
            gen.LabelFormatter = x => LineAxis.FormatElapsed(x);
        }

        Plot.Refresh();
        Plot.UserInputProcessor.IsEnabled = false; // window-based zoom only (Rust parity)
        Plot.Menu = null; // drop ScottPlot's built-in right-click menu so ours is the only one
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Detach();
        _vm = DataContext as LineChartViewModel;
        if (_vm is not null)
        {
            _vm.Updated += Redraw;
            _vm.Reset += OnReset;
            Redraw();
        }
    }

    private void Detach()
    {
        if (_vm is not null)
        {
            _vm.Updated -= Redraw;
        }

        if (_vm is not null)
        {
            _vm.Reset -= OnReset;
        }

        _vm = null;
        _logger?.Clear();
        _lastX = double.NegativeInfinity;
    }

    private void OnReset()
    {
        _logger?.Clear();
        _lastX = double.NegativeInfinity;
    }

    private void OnHover(object? sender, PointerEventArgs e)
    {
        if (_vm is null) { Tip.IsVisible = false; return; }
        var xs = _vm.XsSeconds; var ys = _vm.Ys;
        if (xs.Length == 0) { Tip.IsVisible = false; return; }

        var pos = e.GetPosition(Plot);
        var px = new Pixel((float)pos.X, (float)pos.Y);
        var coord = Plot.Plot.GetCoordinates(px, Plot.Plot.Axes.Bottom, Plot.Plot.Axes.Left);
        int i = NearestSample.IndexOf(xs, coord.X);
        if (i < 0 || i >= ys.Length) { Tip.IsVisible = false; return; }

        TipText.Text = $"{LineAxis.FormatElapsed(xs[i])} · {ys[i]:0.##} {_vm.Unit}";
        Tip.Margin = new Thickness(pos.X + 12, pos.Y + 8, 0, 0);
        Tip.IsVisible = true;
    }

    private void OnHoverLeave(object? sender, PointerEventArgs e)
        => Tip.IsVisible = false;

    // Right-click → zoom menu. LineChartViewModel owns the zoom (Zoom/ZoomBy/ResetZoom),
    // so the menu items act on it directly; the new window applies on the next data tick.
    private void OnChartRightClick(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right || _vm is null)
        {
            return;
        }

        var menu = new ContextMenu();
        void Add(string header, Action act)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => act();
            menu.Items.Add(item);
        }

        Add("Zoom in", () => _vm?.ZoomBy(2.0));
        Add("Zoom out", () => _vm?.ZoomBy(0.5));
        Add("Reset", () => _vm?.ResetZoom());
        menu.Open(this);
        e.Handled = true;
    }

    private void Redraw()
    {
        if (_vm is null || _logger is null)
        {
            return;
        }

        var xs = _vm.XsSeconds;
        var ys = _vm.Ys;

        var start = StreamTail.NewFrom(xs, _lastX);
        if (start == -1) // series reset (re-meta) → clear and re-add
        {
            _logger.Clear();
            _lastX = double.NegativeInfinity;
            start = 0;
        }
        for (int i = start; i < xs.Length && i < ys.Length; i++)
        {
            _logger.Add(xs[i], ys[i]);
        }

        if (xs.Length > 0)
        {
            _lastX = xs[^1];
        }

        Plot.Plot.Axes.SetLimitsX(_vm.WindowMin, _vm.WindowMax);
        Plot.Plot.Axes.AutoScaleY();
        Plot.Refresh();
    }
}
