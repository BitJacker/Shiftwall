using System.Windows.Input;

namespace Shiftwall.Presentation.Mvvm;

public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) execute(parameter);
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Runs an async operation; disabled while running. Exceptions go to <see cref="ErrorHandler"/>.</summary>
public sealed class AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    private bool _running;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    /// <summary>Global handler set by the application (shows a friendly message, logs no config content).</summary>
    public static Action<Exception>? ErrorHandler { get; set; }

    public event EventHandler? CanExecuteChanged;

    public bool IsRunning => _running;

    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync(parameter);

    public async Task ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        RaiseCanExecuteChanged();
        try
        {
            await execute(parameter);
        }
        catch (Exception ex) when (ErrorHandler is not null)
        {
            ErrorHandler(ex);
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
