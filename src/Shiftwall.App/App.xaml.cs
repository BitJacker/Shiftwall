using System.Windows;
using System.Windows.Threading;
using Shiftwall.App.Services;
using Shiftwall.Presentation.Mvvm;
using Shiftwall.Presentation.Services;
using Shiftwall.Presentation.ViewModels;

namespace Shiftwall.App;

public partial class App : Application
{
    private MainViewModel? _main;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        // Errors on background threads: logged (type and frames only); tasks are always awaited, so these are rare.
        AppDomain.CurrentDomain.UnhandledException += (_, args) => { if (args.ExceptionObject is Exception ex) LocalLog.Error(ex, "domain"); };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LocalLog.Error(args.Exception, "task");
            args.SetObserved();
        };
        AsyncRelayCommand.ErrorHandler = ex => Report(ex, "command");

        var dialogs = new WpfDialogService();
        var provider = new DatabaseProvider(DatabaseProvider.DefaultDirectory);
        var db = provider.Load();
        _main = new MainViewModel(db, provider, new SettingsStore(SettingsStore.DefaultPath), dialogs, new FlowDocumentReportPrinter(), new WpfShell());
        ApplyTheme(_main.Theme);
        _main.ThemeChanged += (_, _) => ApplyTheme(_main.Theme);

        // "--page <key>" opens a menu page at start-up (e.g. "--page audit"); used by the CI screenshots.
        var pageArg = Array.IndexOf(e.Args, "--page");
        if (pageArg >= 0 && pageArg + 1 < e.Args.Length && _main.Pages.FirstOrDefault(p => p.Key == e.Args[pageArg + 1]) is { } page)
        {
            _main.SelectedPage = page;
        }

        var window = new MainWindow { DataContext = _main };
        MainWindow = window;
        window.Show();
    }

    private static void ApplyTheme(ThemeChoice theme) =>
        Current.ThemeMode = theme switch
        {
            ThemeChoice.Light => ThemeMode.Light,
            ThemeChoice.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Report(e.Exception, "dispatcher");
        e.Handled = true; // keep the app running; the user sees what to do, not a stack trace
    }

    private static void Report(Exception ex, string where)
    {
        LocalLog.Error(ex, where);
        MessageBox.Show(Current.MainWindow!, LocalLog.FriendlyMessage(ex), "Shiftwall", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
