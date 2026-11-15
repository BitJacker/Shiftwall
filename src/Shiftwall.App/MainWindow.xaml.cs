using System.Windows;

namespace Shiftwall.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        FitToWorkArea();
    }

    /// <summary>
    /// Shrinks the default size on small screens (e.g. 1024x768) so the window is never larger than the work area
    /// and CenterScreen keeps it fully visible.
    /// </summary>
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        Width = Math.Min(Width, area.Width);
        Height = Math.Min(Height, area.Height);
    }
}
