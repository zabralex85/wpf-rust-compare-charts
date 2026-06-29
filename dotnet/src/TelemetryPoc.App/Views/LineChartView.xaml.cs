using System.Windows;
using System.Windows.Controls;
using ScottPlot;
using TelemetryPoc.App.ViewModels;
using TelemetryPoc.App.Viz;

namespace TelemetryPoc.App.Views;

public partial class LineChartView : UserControl
{
    private LineChartViewModel? _vm;
    private ScottPlot.Plottables.DataLogger? _logger;
    private double _lastX = double.NegativeInfinity;

    public LineChartView()
    {
        InitializeComponent();
        StylePlot();
        DataContextChanged += OnDataContextChanged;
        // Re-subscribe when the view re-enters the visual tree with an already-set
        // DataContext (tab-switch / virtualization fires Loaded but not DataContextChanged).
        Loaded += (_, _) => { if (_vm is null) OnDataContextChanged(this, default); };
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
        if (p.Axes.Bottom.TickGenerator is ScottPlot.TickGenerators.NumericAutomatic gen)
            gen.LabelFormatter = x => LineAxis.FormatElapsed(x);
        Plot.Refresh();
        Plot.UserInputProcessor.IsEnabled = false; // window-based zoom only (Rust parity)
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
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
        if (_vm is not null) _vm.Updated -= Redraw;
        if (_vm is not null) _vm.Reset -= OnReset;
        _vm = null;
        _logger?.Clear();
        _lastX = double.NegativeInfinity;
    }

    private void OnReset()
    {
        _logger?.Clear();
        _lastX = double.NegativeInfinity;
    }

    private void OnHover(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_vm is null) { Tip.Visibility = System.Windows.Visibility.Collapsed; return; }
        var xs = _vm.XsSeconds; var ys = _vm.Ys;
        if (xs.Length == 0) { Tip.Visibility = System.Windows.Visibility.Collapsed; return; }

        var pos = e.GetPosition(Plot);
        var px = new ScottPlot.Pixel((float)pos.X, (float)pos.Y);
        var coord = Plot.Plot.GetCoordinates(px, Plot.Plot.Axes.Bottom, Plot.Plot.Axes.Left);
        int i = NearestSample.IndexOf(xs, coord.X);
        if (i < 0 || i >= ys.Length) { Tip.Visibility = System.Windows.Visibility.Collapsed; return; }

        TipText.Text = $"{LineAxis.FormatElapsed(xs[i])} · {ys[i]:0.##} {_vm.Unit}";
        Tip.Margin = new System.Windows.Thickness(pos.X + 12, pos.Y + 8, 0, 0);
        Tip.Visibility = System.Windows.Visibility.Visible;
    }

    private void OnHoverLeave(object sender, System.Windows.Input.MouseEventArgs e)
        => Tip.Visibility = System.Windows.Visibility.Collapsed;

    private void Redraw()
    {
        if (_vm is null || _logger is null) return;
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
            _logger.Add(xs[i], ys[i]);
        if (xs.Length > 0) _lastX = xs[^1];

        Plot.Plot.Axes.SetLimitsX(_vm.WindowMin, _vm.WindowMax);
        Plot.Plot.Axes.AutoScaleY();
        Plot.Refresh();
    }
}
