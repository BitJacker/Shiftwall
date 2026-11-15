using System.Collections.ObjectModel;
using System.Globalization;
using Shiftwall.Core.Data;
using Shiftwall.Presentation.Localization;
using Shiftwall.Presentation.Mvvm;
using Shiftwall.Presentation.Services;

namespace Shiftwall.Presentation.ViewModels;

/// <summary>An entry of the side menu.</summary>
/// <param name="TitleKey">Localization key of the title.</param>
/// <param name="Glyph">Segoe Fluent Icons / Segoe MDL2 Assets code point.</param>
/// <param name="Content">The page view model, or null for a page not available yet.</param>
public sealed record NavItem(string Key, string TitleKey, string Glyph, object? Content, bool IsToolbox = false)
{
    public bool IsAvailable => Content is not null;
}

/// <summary>Placeholder for toolbox tools delivered in a later phase.</summary>
public sealed class ComingSoonViewModel(string titleKey) : ObservableObject
{
    public string TitleKey { get; } = titleKey;
}

public sealed class MainViewModel : ObservableObject
{
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private NavItem _selected;

    public MainViewModel(ModelDatabase db, DatabaseProvider dbProvider, SettingsStore settingsStore, IDialogService dialogs, IReportPrinter printer, IShell shell)
    {
        _settingsStore = settingsStore;
        _settings = settingsStore.Load();
        Loc.Instance.Culture = CultureInfo.GetCultureInfo(_settings.Language is "en" ? "en" : "it-IT");

        Wizard = new MigrationWizardViewModel(db, dialogs, printer, shell);
        Batch = new BatchViewModel(db, dialogs, shell);
        About = new AboutViewModel(db, dbProvider, dialogs, shell, newDb =>
        {
            Wizard.Target.UpdateDatabase(newDb);
            Batch.Target.UpdateDatabase(newDb);
        });

        Pages =
        [
            new("migration", "Nav.Migration", "", Wizard),
            new("batch", "Nav.Batch", "", Batch),
            new("compare", "Nav.Compare", "", null, true),
            new("audit", "Nav.Audit", "", null, true),
            new("cleanup", "Nav.Cleanup", "", null, true),
            new("export", "Nav.Export", "", null, true),
            new("import", "Nav.Import", "", null, true),
            new("vpn", "Nav.Vpn", "", null, true),
            new("anonymize", "Nav.Anonymize", "", null, true),
            new("snippets", "Nav.Snippets", "", null, true),
            new("upgrade", "Nav.Upgrade", "", null, true),
            new("about", "Nav.About", "", About),
        ];
        _selected = Pages[0];
        ToggleLanguageCommand = new RelayCommand(() => Language = Language == "it-IT" ? "en" : "it-IT");
    }

    public event EventHandler? ThemeChanged;

    public MigrationWizardViewModel Wizard { get; }

    public BatchViewModel Batch { get; }

    public AboutViewModel About { get; }

    public ObservableCollection<NavItem> Pages { get; }

    public RelayCommand ToggleLanguageCommand { get; }

    public NavItem SelectedPage
    {
        get => _selected;
        set
        {
            if (value is null) return;
            Set(ref _selected, value);
            OnPropertyChanged(nameof(CurrentContent));
        }
    }

    public object CurrentContent => _selected.Content ?? new ComingSoonViewModel(_selected.TitleKey);

    public ThemeChoice Theme
    {
        get => _settings.Theme;
        set
        {
            if (_settings.Theme == value) return;
            _settings.Theme = value;
            _settingsStore.Save(_settings);
            OnPropertyChanged();
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IReadOnlyList<ThemeChoice> Themes { get; } = [ThemeChoice.System, ThemeChoice.Light, ThemeChoice.Dark];

    public string Language
    {
        get => _settings.Language is "en" ? "en" : "it-IT";
        set
        {
            _settings.Language = value;
            _settingsStore.Save(_settings);
            Loc.Instance.Culture = CultureInfo.GetCultureInfo(value);
            OnPropertyChanged();
        }
    }
}

public sealed class AboutViewModel : ObservableObject
{
    public const string RepositoryUrl = "https://github.com/BitJacker/shiftwall";
    public const string UpgradeToolUrl = "https://docs.fortinet.com/upgrade-tool/fortigate";

    private readonly DatabaseProvider _provider;
    private readonly IDialogService _dialogs;
    private readonly Action<ModelDatabase> _onDatabaseChanged;
    private ModelDatabase _db;

    public AboutViewModel(ModelDatabase db, DatabaseProvider provider, IDialogService dialogs, IShell shell, Action<ModelDatabase> onDatabaseChanged)
    {
        _db = db;
        _provider = provider;
        _dialogs = dialogs;
        _onDatabaseChanged = onDatabaseChanged;
        OpenRepositoryCommand = new RelayCommand(() => shell.OpenUrl(RepositoryUrl));
        ImportDatabaseCommand = new RelayCommand(ImportDatabase);
        ResetDatabaseCommand = new RelayCommand(() => Changed(_provider.Reset()), () => _provider.UsingImported);
        Loc.Instance.PropertyChanged += (_, _) => OnPropertyChanged(nameof(DatabaseSummary));
    }

    public string Version => MigrationWizardViewModel.AppVersion;

    public RelayCommand OpenRepositoryCommand { get; }

    public RelayCommand ImportDatabaseCommand { get; }

    public RelayCommand ResetDatabaseCommand { get; }

    public string DatabaseSummary => Loc.F(
        _provider.UsingImported ? "About.Database.Imported" : "About.Database.Embedded",
        _db.Models.Count,
        _db.Releases.Count,
        _db.Releases.Count == 0 ? "-" : _db.Releases[^1].Version);

    private void ImportDatabase()
    {
        if (_dialogs.OpenFile(Loc.T("About.Database.Pick"), "models.json|models.json") is not { } path) return;
        try
        {
            Changed(_provider.Import(path));
            _dialogs.ShowInfo(Loc.T("About.Database.ImportOk"));
        }
        catch (DatabaseException ex)
        {
            var first = ex.Errors.Take(5).ToList();
            _dialogs.ShowError(Loc.T("About.Database.ImportFailed") + (first.Count > 0 ? "\n\n" + string.Join("\n", first) : ""));
        }
    }

    private void Changed(ModelDatabase db)
    {
        _db = db;
        _onDatabaseChanged(db);
        OnPropertyChanged(nameof(DatabaseSummary));
        ResetDatabaseCommand.RaiseCanExecuteChanged();
    }
}
