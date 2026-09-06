using CommunityToolkit.Mvvm.Input;

namespace LizTerm.App;

/// <summary>CommunityToolkit's ExecuteAsync ignores CanExecute, which a button honours but a hotkey handler does
/// not; every command reached from a keystroke goes through here (spec 7).</summary>
public static class CommandRouting
{
    public static Task TryExecuteAsync(IAsyncRelayCommand command, object? parameter = null) =>
        command.CanExecute(parameter) ? command.ExecuteAsync(parameter) : Task.CompletedTask;

    public static Task TryExecuteAsync<T>(IAsyncRelayCommand<T> command, T parameter) =>
        command.CanExecute(parameter) ? command.ExecuteAsync(parameter) : Task.CompletedTask;

    public static void TryExecute(IRelayCommand command, object? parameter = null)
    {
        if (command.CanExecute(parameter)) command.Execute(parameter);
    }
}
