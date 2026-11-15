using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Shiftwall.Presentation.ViewModels;

namespace Shiftwall.App.Views;

public partial class MigrationWizardView : UserControl
{
    private const string PortFormat = "Shiftwall.TargetPort";

    public MigrationWizardView() => InitializeComponent();

    private MigrationWizardViewModel? Vm => DataContext as MigrationWizardViewModel;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnSourceDrop(object sender, DragEventArgs e)
    {
        if (Vm is { } vm && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            await vm.DropFileAsync(files[0], asTarget: false);
        }
    }

    // Drag a target port onto a source row to map it (the combo box offers the same without a mouse).
    private void OnTargetPortMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not ListBox list) return;
        if (list.SelectedItem is TargetPortItem port)
        {
            DragDrop.DoDragDrop(list, new DataObject(PortFormat, port.Name), DragDropEffects.Link);
        }
    }

    private void OnPortDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(PortFormat) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnPortDrop(object sender, DragEventArgs e)
    {
        if (Vm is { } vm && sender is FrameworkElement { DataContext: SourcePortItem row } && e.Data.GetData(PortFormat) is string target)
        {
            vm.Map(row.Name, target);
        }
    }
}
