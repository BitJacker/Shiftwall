using System.Collections.ObjectModel;
using Shiftwall.Core.Config;
using Shiftwall.Presentation.Localization;
using Shiftwall.Presentation.Mvvm;
using Shiftwall.Presentation.Services;

namespace Shiftwall.Presentation.ViewModels.Toolbox;

/// <summary>
/// A configuration file loaded by a toolbox page (browse or drag &amp; drop). The file is read once and kept in memory;
/// tools that change the tree get a fresh parse with <see cref="ParseFresh"/>.
/// </summary>
public sealed class ConfigSlotViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly string _titleKey;
    private ConfigInspection? _inspection;
    private string? _path;

    public ConfigSlotViewModel(IDialogService dialogs, string titleKey)
    {
        _dialogs = dialogs;
        _titleKey = titleKey;
        BrowseCommand = new AsyncRelayCommand(async () =>
        {
            if (_dialogs.OpenFile(Loc.T(_titleKey), MigrationWizardViewModel.ConfigFilter) is { } p) await LoadAsync(p);
        });
        ClearCommand = new RelayCommand(Clear, () => IsLoaded);
        Loc.Instance.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Summary));
    }

    public event EventHandler? Changed;

    public string TitleKey => _titleKey;

    public AsyncRelayCommand BrowseCommand { get; }

    public RelayCommand ClearCommand { get; }

    public ObservableCollection<MessageItem> Diagnostics { get; } = [];

    public string? Path { get => _path; private set => Set(ref _path, value); }

    public string? FileName => _path is null ? null : System.IO.Path.GetFileName(_path);

    public ConfigInspection? Inspection => _inspection;

    /// <summary>Parsed document, shared: tools must not change it (use <see cref="ParseFresh"/>).</summary>
    public ConfigDocument? Document => _inspection?.Document;

    public bool IsLoaded => _inspection is { Kind: ConfigFileKind.FortiOsText, Document: not null };

    public string Summary
    {
        get
        {
            if (_inspection is null) return Loc.T("Tool.Slot.None");
            if (!IsLoaded) return Loc.T("Tool.Slot.Unusable");
            var h = _inspection.Header;
            return Loc.F("Tool.Slot.Summary", h?.PlatformCode ?? "?", h?.Version ?? "?", _inspection.Document!.LineCount);
        }
    }

    /// <summary>A new, independent parse of the same text.</summary>
    public ConfigDocument ParseFresh() => ConfigParser.Parse(_inspection!.Text.Text);

    public async Task LoadAsync(string path)
    {
        var bytes = await InputFile.ReadAllBytesAsync(path);
        var inspection = await Task.Run(() => ConfigInspector.Inspect(bytes));
        _inspection = inspection;
        Path = path;
        Diagnostics.Clear();
        foreach (var d in inspection.Diagnostics.Where(d => d.Severity != DiagnosticSeverity.Info)) Diagnostics.Add(MessageItem.From(d));
        Raise();
    }

    public void Clear()
    {
        _inspection = null;
        Path = null;
        Diagnostics.Clear();
        Raise();
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(Document));
        ClearCommand.RaiseCanExecuteChanged();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Shared helpers for toolbox pages that write files.</summary>
internal static class ToolOutput
{
    public const string ScriptFilter = "CLI script (*.txt)|*.txt|All files (*.*)|*.*";

    /// <summary>Saves through <see cref="Shiftwall.Features.Migration.SafeOutput"/>: never overwrites the loaded configurations.</summary>
    public static bool Save(IDialogService dialogs, string titleKey, string filter, string suggestedName, byte[] bytes, IEnumerable<string?> protectedPaths)
    {
        if (dialogs.SaveFile(Loc.T(titleKey), filter, suggestedName) is not { } path) return false;
        try
        {
            Shiftwall.Features.Migration.SafeOutput.Write(path, bytes, protectedPaths.Where(p => p is not null)!, allowOverwrite: true);
            dialogs.ShowInfo(Loc.F("Tool.Saved", System.IO.Path.GetFileName(path)));
            return true;
        }
        catch (Shiftwall.Features.Migration.UnsafeOutputException)
        {
            dialogs.ShowError(Loc.T("Error.WouldOverwriteOriginal"));
            return false;
        }
    }

    public static byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    /// <summary>"name.suffix.ext" next to <paramref name="source"/>.</summary>
    public static string Suggest(string? source, string suffix, string extension)
    {
        if (source is null) return $"shiftwall-{suffix}{extension}";
        var dir = System.IO.Path.GetDirectoryName(source) ?? "";
        return System.IO.Path.Combine(dir, $"{System.IO.Path.GetFileNameWithoutExtension(source)}.{suffix}{extension}");
    }

    /// <summary>Header as CLI comment lines ("# …"), ending with a newline.</summary>
    public static string ScriptHeaderLines(string toolKey, string? source) =>
        string.Concat(ScriptHeader(toolKey, source).Split('\n').Select(l => "# " + l + "\n"));

    /// <summary>Runs a fire-and-forget refresh from an event handler; errors go to the global handler.</summary>
    public static async void Run(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex) when (AsyncRelayCommand.ErrorHandler is not null)
        {
            AsyncRelayCommand.ErrorHandler(ex);
        }
    }

    /// <summary>Header lines of generated scripts.</summary>
    public static string ScriptHeader(string toolKey, string? source) =>
        $"{Loc.F("Tool.Script.Header", Loc.T(toolKey), MigrationWizardViewModel.AppVersion, DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture))}\n" +
        (source is null ? "" : Loc.F("Tool.Script.Source", System.IO.Path.GetFileName(source)) + "\n") +
        Loc.T("Tool.Script.Review");
}
