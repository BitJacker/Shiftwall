using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using Shiftwall.Features.Migration;
using Shiftwall.Presentation.Localization;
using Shiftwall.Presentation.Services;

namespace Shiftwall.App.Services;

public sealed class WpfDialogService : IDialogService
{
    private static Window? Owner => Application.Current?.MainWindow;

    public string? OpenFile(string title, string filter)
    {
        var d = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return d.ShowDialog(Owner) == true ? d.FileName : null;
    }

    public IReadOnlyList<string> OpenFiles(string title, string filter)
    {
        var d = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true, Multiselect = true };
        return d.ShowDialog(Owner) == true ? d.FileNames : [];
    }

    public string? SaveFile(string title, string filter, string suggestedPath)
    {
        var d = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = Path.GetFileName(suggestedPath),
            InitialDirectory = Path.GetDirectoryName(suggestedPath),
            OverwritePrompt = true,
            AddExtension = true,
        };
        return d.ShowDialog(Owner) == true ? d.FileName : null;
    }

    public string? PickFolder(string title)
    {
        var d = new OpenFolderDialog { Title = title };
        return d.ShowDialog(Owner) == true ? d.FolderName : null;
    }

    public void ShowError(string message) =>
        MessageBox.Show(Owner!, message, "Shiftwall", MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowInfo(string message) =>
        MessageBox.Show(Owner!, message, "Shiftwall", MessageBoxButton.OK, MessageBoxImage.Information);

    public bool Confirm(string message) =>
        MessageBox.Show(Owner!, message, "Shiftwall", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}

/// <summary>Opens links and folders. Only ever called from an explicit user click.</summary>
public sealed class WpfShell : IShell
{
    public void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    public void OpenFolder(string path)
    {
        if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}

/// <summary>
/// Prints the report (without the diff) as a FlowDocument. Choosing "Microsoft Print to PDF" in the print
/// dialog produces the PDF, with no third-party PDF library.
/// </summary>
public sealed class FlowDocumentReportPrinter : IReportPrinter
{
    public void Print(MigrationRequest request, MigrationResult result, ReportContext ctx)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        var doc = Build(request, result, ctx);
        doc.PageHeight = dialog.PrintableAreaHeight;
        doc.PageWidth = dialog.PrintableAreaWidth;
        doc.PagePadding = new Thickness(56);
        doc.ColumnWidth = double.PositiveInfinity;
        dialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, MigrationText.Get("Report.Title", ctx.Culture));
    }

    internal static FlowDocument Build(MigrationRequest request, MigrationResult result, ReportContext ctx)
    {
        var c = ctx.Culture;
        string T(string key) => MigrationText.Get(key, c);
        var doc = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 11, Foreground = Brushes.Black, Background = Brushes.White };
        doc.Blocks.Add(new Paragraph(new Run(T("Report.Title"))) { FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x76, 0x6E)) });
        doc.Blocks.Add(new Paragraph(new Run(string.Format(c, T("Report.Generated"), ctx.AppVersion, ctx.GeneratedAt.ToString("g", c)))) { Foreground = Brushes.DimGray });

        var table = new Table { CellSpacing = 0 };
        table.Columns.Add(new TableColumn { Width = new GridLength(120) });
        table.Columns.Add(new TableColumn());
        table.Columns.Add(new TableColumn());
        var rows = new TableRowGroup();
        rows.Rows.Add(Row("", T("Report.Source"), T("Report.Target"), bold: true));
        rows.Rows.Add(Row("File", ctx.SourceName, ctx.TargetName));
        rows.Rows.Add(Row(T("Report.Model"), request.Source.PlatformCode ?? "?", request.Target.ModelId ?? request.Target.PlatformCode ?? "?"));
        rows.Rows.Add(Row(T("Report.Firmware"), request.Source.Version ?? "?", request.Target.Version ?? "?"));
        table.RowGroups.Add(rows);
        doc.Blocks.Add(table);

        Section(doc, T("Report.PreChecks"), result.Analysis.PreChecks, c);
        var map = new List(new ListItem()) { MarkerStyle = TextMarkerStyle.None };
        map.ListItems.Clear();
        foreach (var p in request.Source.PhysicalInterfaces)
        {
            var to = request.Mapping[p.Name] ?? T("Report.NotMapped");
            map.ListItems.Add(new ListItem(new Paragraph(new Run($"{p.Name}  →  {to}"))));
        }

        doc.Blocks.Add(Heading(T("Report.Mapping")));
        doc.Blocks.Add(map);
        Section(doc, null, result.Analysis.MappingFindings, c);
        Section(doc, T("Report.Changes"), result.Changes, c);
        Section(doc, T("Report.Validation"), result.Validation, c);
        doc.Blocks.Add(Heading(T("Report.Checklist")));
        foreach (var item in result.Checklist) doc.Blocks.Add(new Paragraph(new Run("☐  " + item.Format(c))) { Margin = new Thickness(0, 2, 0, 2) });
        doc.Blocks.Add(new Paragraph(new Run(T("Report.Disclaimer"))) { Foreground = Brushes.DimGray, Margin = new Thickness(0, 24, 0, 0) });
        return doc;

        static TableRow Row(string a, string b, string c2, bool bold = false)
        {
            var r = new TableRow();
            foreach (var t in new[] { a, b, c2 })
            {
                r.Cells.Add(new TableCell(new Paragraph(new Run(t)) { FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal }) { Padding = new Thickness(2) });
            }

            return r;
        }
    }

    private static Paragraph Heading(string text) =>
        new(new Run(text)) { FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 4) };

    private static void Section(FlowDocument doc, string? title, IEnumerable<MigrationFinding> findings, CultureInfo c)
    {
        if (title is not null) doc.Blocks.Add(Heading(title));
        foreach (var f in findings)
        {
            var mark = f.Severity switch { FindingSeverity.Error => "✖", FindingSeverity.Warning => "⚠", _ => "ℹ" };
            doc.Blocks.Add(new Paragraph(new Run($"{mark}  {f.Code}  {f.Format(c)}")) { Margin = new Thickness(0, 2, 0, 2) });
        }
    }
}

/// <summary>
/// Local error log (%LOCALAPPDATA%\Shiftwall\logs). Records exception types and stack frames only: never
/// exception messages or configuration content.
/// </summary>
public static class LocalLog
{
    private static readonly object Gate = new();

    public static string Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shiftwall", "logs");

    public static void Error(Exception ex, string where)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                var path = Path.Combine(Directory, "shiftwall.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".1", overwrite: true);
                var frames = new StackTrace(ex, false).GetFrames().Select(f => f.GetMethod()).Where(m => m is not null).Select(m => $"   at {m!.DeclaringType?.FullName}.{m.Name}");
                File.AppendAllLines(path, [$"{DateTimeOffset.Now:O} {where} {ex.GetType().FullName}", .. frames]);
            }
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            // Logging must never crash the app.
        }
    }

    /// <summary>User-facing text for an unexpected error: what happened and what to do, no stack trace.</summary>
    public static string FriendlyMessage(Exception ex) => ex switch
    {
        FileNotFoundException or DirectoryNotFoundException => Loc.T("Error.FileNotFound"),
        UnauthorizedAccessException => Loc.T("Error.AccessDenied"),
        IOException => Loc.T("Error.FileInUse"),
        OutOfMemoryException => Loc.T("Error.OutOfMemory"),
        _ => Loc.F("Error.Unexpected", Directory),
    };
}
