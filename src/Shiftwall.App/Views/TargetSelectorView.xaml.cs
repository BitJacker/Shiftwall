using System.Windows;
using System.Windows.Controls;
using Shiftwall.Presentation.ViewModels;

namespace Shiftwall.App.Views;

public partial class TargetSelectorView : UserControl
{
    public TargetSelectorView() => InitializeComponent();

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnBackupDrop(object sender, DragEventArgs e)
    {
        if (DataContext is TargetSelectorViewModel vm && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            await vm.LoadBackupAsync(files[0]);
        }
    }
}
