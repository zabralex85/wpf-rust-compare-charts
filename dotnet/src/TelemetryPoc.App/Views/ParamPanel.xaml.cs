using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TelemetryPoc.App.ViewModels;

namespace TelemetryPoc.App.Views;

public partial class ParamPanel : UserControl
{
    public ParamPanel() => InitializeComponent();

    private void OnRowDrag(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (sender is FrameworkElement fe && fe.DataContext is ParamRowViewModel row)
        {
            var data = new DataObject("inu-channel", row.ChannelId);
            DragDrop.DoDragDrop(fe, data, DragDropEffects.Copy);
        }
    }
}
