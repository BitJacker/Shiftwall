using System.Windows;
using System.Windows.Controls;
using Shiftwall.Presentation.ViewModels.Toolbox;

namespace Shiftwall.App.Views;

public partial class ConfigSlotView : UserControl
{
    public ConfigSlotView() => InitializeComponent();

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is ConfigSlotViewModel vm && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            try
            {
                await vm.LoadAsync(files[0]);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                Shiftwall.Presentation.Mvvm.AsyncRelayCommand.ErrorHandler?.Invoke(ex);
            }
        }
    }
}
