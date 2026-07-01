using Avalonia.Controls;
using Avalonia.Input;
using TelemetryPoc.App.ViewModels;

namespace TelemetryPoc.App.Views;

public partial class ParamPanel : UserControl
{
    public ParamPanel()
    {
        InitializeComponent();
    }

    private async void OnRowDrag(object? sender, PointerEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (sender is Control fe && fe.DataContext is ParamRowViewModel row)
        {
            var data = new DataObject();
            data.Set("inu-channel", row.ChannelId);
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
        }
    }
}
