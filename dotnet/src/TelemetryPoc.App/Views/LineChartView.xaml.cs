using System.Windows;
using System.Windows.Controls;
using ScottPlot;
using TelemetryPoc.App.ViewModels;
using TelemetryPoc.App.Viz;

namespace TelemetryPoc.App.Views;

public partial class LineChartView : UserControl
{
    private LineChartViewModel? _vm;

    public LineChartView()
    {
        InitializeComponent();
        StylePlot();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => Detach();
    }

    private void StylePlot()
    {
        var p = Plot.Plot;
        p.FigureBackground.Color = Color.FromHex("#10151d");
        p.DataBackground.Color = Color.FromHex("#0c121a");
        p.Axes.Color(Color.FromHex("#566273"));
        p.Grid.MajorLineColor = Color.FromHex("#1d2632");
        // relative m:ss x-axis labels
        if (p.Axes.Bottom.TickGenerator is ScottPlot.TickGenerators.NumericAutomatic gen)
            gen.LabelFormatter = x => LineAxis.FormatElapsed(x);
        Plot.Refresh();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();
        _vm = DataContext as LineChartViewModel;
        if (_vm is not null)
        {
            _vm.Updated += Redraw;
            Redraw();
        }
    }

    private void Detach()
    {
        if (_vm is not null) _vm.Updated -= Redraw;
        _vm = null;
    }

    private void Redraw()
    {
        if (_vm is null) return;
        var p = Plot.Plot;
        p.Clear();
        if (_vm.XsSeconds.Length > 0)
        {
            var sc = p.Add.Scatter(_vm.XsSeconds, _vm.Ys);
            sc.Color = Color.FromHex("#38c5e0");
            sc.LineWidth = 1.5f;
            sc.MarkerSize = 0;
            p.Axes.AutoScaleY();
            p.Axes.SetLimitsX(_vm.WindowMin, _vm.WindowMax);
        }
        Plot.Refresh();
    }
}
