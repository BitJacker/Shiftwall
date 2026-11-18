namespace Shiftwall.Presentation.Services;

/// <summary>File/folder pickers and messages, implemented by the WPF app.</summary>
public interface IDialogService
{
    string? OpenFile(string title, string filter);

    IReadOnlyList<string> OpenFiles(string title, string filter);

    string? SaveFile(string title, string filter, string suggestedPath);

    string? PickFolder(string title);

    void ShowError(string message);

    void ShowInfo(string message);

    bool Confirm(string message);
}

/// <summary>Prints the migration report (no diff) to a printer chosen by the user, e.g. "Microsoft Print to PDF".</summary>
public interface IReportPrinter
{
    void Print(Shiftwall.Features.Migration.MigrationRequest request, Shiftwall.Features.Migration.MigrationResult result, Shiftwall.Features.Migration.ReportContext context);
}

/// <summary>Opens a URL in the user's browser. Called only after an explicit user click.</summary>
public interface IShell
{
    void OpenUrl(string url);

    void OpenFolder(string path);

    /// <summary>Copies text (a generated CLI script) to the clipboard.</summary>
    void CopyText(string text);
}

/// <summary>Marshals work to the UI thread.</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}
