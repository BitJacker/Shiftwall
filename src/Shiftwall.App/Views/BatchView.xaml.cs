using System.Windows;
using System.Windows.Controls;
using Shiftwall.Presentation.ViewModels;

namespace Shiftwall.App.Views;

public partial class BatchView : UserControl
{
    public BatchView() => InitializeComponent();

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFilesDrop(object sender, DragEventArgs e)
    {
        if (DataContext is BatchViewModel vm && e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            vm.AddDroppedFiles(files);
        }
    }
}
