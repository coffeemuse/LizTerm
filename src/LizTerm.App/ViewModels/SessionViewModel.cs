using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.Status;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

public partial class SessionViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IEmulatorSession _session;
    private readonly Action<Action> _dispatch;

    [ObservableProperty] private ScreenSnapshot? _screen;
    [ObservableProperty] private string _connectionText = "";
    [ObservableProperty] private string _tlsText = "";
    [ObservableProperty] private string _keyboardText = "";
    [ObservableProperty] private string _insertText = "";
    [ObservableProperty] private string _cursorText = "";
    [ObservableProperty] private string _modelText = "";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isConnected;

    /// <param name="dispatch">Marshals a callback onto the UI thread. Tests pass <c>a => a()</c>.</param>
    public SessionViewModel(IEmulatorSession session, Action<Action> dispatch)
    {
        _session = session;
        _dispatch = dispatch;

        session.ScreenUpdated += (_, s) => _dispatch(() => ApplyScreen(s));
        session.StatusChanged += (_, k) => _dispatch(() => ApplyStatus(k));
        session.ConnectionChanged += (_, c) => _dispatch(() => ApplyConnection(c));
        session.Faulted += (_, f) => _dispatch(() => ErrorMessage = StatusFormatter.Fault(f));
        session.HostMessage += (_, m) => _dispatch(() => ErrorMessage = m);

        ApplyScreen(session.CurrentScreen);
        ApplyStatus(session.KeyboardStatus);
        ApplyConnection(session.ConnectionState);
    }

    public SessionProfile Profile => _session.Profile;
    public string Title => $"{Profile.Name} - {Profile.Host}";

    private void ApplyScreen(ScreenSnapshot snapshot)
    {
        Screen = snapshot;
        CursorText = StatusFormatter.Cursor(snapshot.Cursor);
    }

    private void ApplyStatus(KeyboardStatus status)
    {
        KeyboardText = StatusFormatter.Keyboard(status);
        InsertText = StatusFormatter.Insert(status.InsertMode);
        ModelText = StatusFormatter.Model(Profile, status.LuName);
    }

    private void ApplyConnection(ConnectionState state)
    {
        IsConnected = state.IsConnected();
        ConnectionText = StatusFormatter.Connection(state, Profile.Host);
        TlsText = StatusFormatter.Tls(_session.Tls);
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        ErrorMessage = null;
        try
        {
            await _session.ConnectAsync();
        }
        catch (ConnectionFailedException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (BackendUnavailableException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private Task DisconnectAsync() => Guard(_session.DisconnectAsync());

    [RelayCommand]
    private Task SendKeyAsync(TerminalKey key) => Guard(_session.SendKeyAsync(key));

    [RelayCommand]
    private void DismissError() => ErrorMessage = null;

    public Task TypeTextAsync(string text) => Guard(_session.TypeTextAsync(text));

    public Task MoveCursorAsync(int row, int column) => Guard(_session.MoveCursorAsync(row, column));

    /// <summary>Rejected actions are not errors to show: b3270 already explains them through the keyboard lock.</summary>
    private async Task Guard(Task action)
    {
        try
        {
            await action;
        }
        catch (EmulatorActionException)
        {
        }
        catch (InvalidOperationException)
        {
            // Session not started yet; nothing to send to.
        }
        catch (BackendUnavailableException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public ValueTask DisposeAsync() => _session.DisposeAsync();
}
