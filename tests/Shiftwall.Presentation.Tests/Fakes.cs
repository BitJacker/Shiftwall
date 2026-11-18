using Shiftwall.Features.Migration;
using Shiftwall.Presentation.Services;

namespace Shiftwall.Presentation.Tests;

internal sealed class FakeDialogs : IDialogService
{
    public Queue<string?> OpenAnswers { get; } = new();

    public Queue<string?> SaveAnswers { get; } = new();

    public List<string> Errors { get; } = [];

    public List<string> Infos { get; } = [];

    public string? OpenFile(string title, string filter) => OpenAnswers.Count > 0 ? OpenAnswers.Dequeue() : null;

    public IReadOnlyList<string> OpenFiles(string title, string filter) => OpenAnswers.Count > 0 && OpenAnswers.Dequeue() is { } f ? [f] : [];

    public string? SaveFile(string title, string filter, string suggestedPath) => SaveAnswers.Count > 0 ? SaveAnswers.Dequeue() : null;

    public string? PickFolder(string title) => OpenAnswers.Count > 0 ? OpenAnswers.Dequeue() : null;

    public void ShowError(string message) => Errors.Add(message);

    public void ShowInfo(string message) => Infos.Add(message);

    public bool ConfirmAnswer { get; set; } = true;

    public List<string> Confirmations { get; } = [];

    public bool Confirm(string message)
    {
        Confirmations.Add(message);
        return ConfirmAnswer;
    }
}

internal sealed class FakePrinter : IReportPrinter
{
    public int Printed { get; private set; }

    public void Print(MigrationRequest request, MigrationResult result, ReportContext context) => Printed++;
}

internal sealed class FakeShell : IShell
{
    public List<string> Urls { get; } = [];

    public void OpenUrl(string url) => Urls.Add(url);

    public List<string> Copied { get; } = [];

    public void OpenFolder(string path)
    {
    }

    public void CopyText(string text) => Copied.Add(text);
}
