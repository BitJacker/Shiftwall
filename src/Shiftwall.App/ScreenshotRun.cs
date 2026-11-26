using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Shiftwall.Presentation.ViewModels;

namespace Shiftwall.App;

/// <summary>
/// CI only: "--wizard-screenshots &lt;source&gt; &lt;target&gt; &lt;folder&gt;" goes through every step of the rebuild
/// wizard with the synthetic fixtures, in the three switch variants, and saves one PNG per screen. Passwords are hidden
/// before any screen that could show them. Nothing else is written, no network is used.
/// </summary>
internal static class ScreenshotRun
{
    public static bool TryParse(string[] args, out (string Source, string Target, string Folder) run)
    {
        var i = Array.IndexOf(args, "--wizard-screenshots");
        run = i >= 0 && i + 3 < args.Length ? (args[i + 1], args[i + 2], args[i + 3]) : default;
        return i >= 0 && i + 3 < args.Length;
    }

    public static async Task RunAsync(Window window, MainViewModel main, (string Source, string Target, string Folder) run)
    {
        Directory.CreateDirectory(run.Folder);
        window.Width = 1440;
        window.Height = 1080;
        window.WindowState = WindowState.Normal;
        main.SelectedPage = main.Pages.First(p => p.Key == "migration");
        var w = main.Wizard;
        var n = 0;
        async Task Shot(string name)
        {
            w.HidePasswords();
            await Settle();
            Save(window, Path.Combine(run.Folder, $"wizard-{++n:00}-{name}.png"));
        }

        await Shot("load-empty");
        await w.LoadSourceAsync(run.Source);
        await w.Target.LoadBackupAsync(run.Target);
        await w.ReanalyzeAsync();
        await Shot("load");
        w.Step = WizardStep.PreChecks;
        await Shot("prechecks");

        foreach (var variant in new[] { "lan", "recreated", "split" })
        {
            Configure(w, variant);
            await w.ReanalyzeAsync();
            w.Step = WizardStep.Mapping;
            await Shot($"{variant}-mapping");
            ScrollToEnd(window);
            await Shot($"{variant}-mapping-end");
            await w.NextCommand.ExecuteAsync(null);
            if (w.Step != WizardStep.Transform)
            {
                await Shot($"{variant}-blocked");
                continue;
            }

            var tabs = Descendants<TabControl>(window).Where(t => t.IsVisible && t.Items.Count == 4).ToList();
            foreach (var t in tabs)
            {
                for (var i = 0; i < t.Items.Count; i++)
                {
                    t.SelectedIndex = i;
                    await Shot($"{variant}-transform-{i + 1}");
                }
            }

            w.Step = WizardStep.Report;
            await Shot($"{variant}-save");
            w.Step = WizardStep.Mapping;
        }
    }

    /// <summary>The three variants of the 110C internal switch on the 40F (same as the golden tests).</summary>
    private static void Configure(MigrationWizardViewModel w, string variant)
    {
        var internalRow = w.Row("internal")!;
        foreach (var c in internalRow.PortChoices) c.IsChecked = false;
        internalRow.Action = SwitchAction.Map;
        switch (variant)
        {
            case "lan":
                internalRow.Target = "lan";
                w.Row("wan2")!.Target = "lan3";
                w.ManagementInterface = "lan";
                break;
            case "recreated":
                w.Row("wan2")!.Target = "lan";
                internalRow.Action = SwitchAction.Recreate;
                foreach (var c in internalRow.PortChoices) c.IsChecked = c.Port is "lan1" or "lan2";
                w.ManagementInterface = "internal";
                break;
            case "split":
                w.Row("wan2")!.Target = "lan";
                internalRow.Action = SwitchAction.Split;
                foreach (var c in internalRow.PortChoices) c.IsChecked = c.Port is "lan1" or "lan2";
                w.ManagementInterface = "lan1";
                break;
        }
    }

    private static void ScrollToEnd(Window window)
    {
        foreach (var s in Descendants<ScrollViewer>(window).Where(s => s.IsVisible)) s.ScrollToEnd();
    }

    private static async Task Settle()
    {
        for (var i = 0; i < 3; i++)
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(150);
        }
    }

    private static void Save(Window window, string path)
    {
        var root = (FrameworkElement)window.Content;
        root.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(window.Background ?? Brushes.White, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }

        bmp.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(path);
        png.Save(f);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var d in Descendants<T>(c)) yield return d;
        }
    }
}
