using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.Clipboard;
using LizTerm.App.Status;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

public partial class SessionViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IEmulatorSession _session;
    private readonly Action<Action> _dispatch;
    private readonly ITextClipboard _clipboard;
    private readonly EventHandler<ScreenSnapshot> _onScreenUpdated;
    private readonly EventHandler<KeyboardStatus> _onStatusChanged;
    private readonly EventHandler<ConnectionState> _onConnectionChanged;
    private readonly EventHandler<BackendFault> _onFaulted;
    private readonly EventHandler<string> _onHostMessage;
    private bool _disposed;

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
    /// <param name="clipboard">Text clipboard; the app passes <see cref="AvaloniaTextClipboard"/>, tests a fake.</param>
    public SessionViewModel(IEmulatorSession session, Action<Action> dispatch, ITextClipboard clipboard)
    {
        _session = session;
        _dispatch = dispatch;
        _clipboard = clipboard;

        _onScreenUpdated = (_, s) => _dispatch(() => ApplyScreen(s));
        _onStatusChanged = (_, k) => _dispatch(() => ApplyStatus(k));
        _onConnectionChanged = (_, c) => _dispatch(() => ApplyConnection(c));
        _onFaulted = (_, f) => _dispatch(() =>
        {
            if (_disposed) return;
            ErrorMessage = StatusFormatter.Fault(f);
        });
        _onHostMessage = (_, m) => _dispatch(() =>
        {
            if (_disposed) return;
            ErrorMessage = m;
        });

        session.ScreenUpdated += _onScreenUpdated;
        session.StatusChanged += _onStatusChanged;
        session.ConnectionChanged += _onConnectionChanged;
        session.Faulted += _onFaulted;
        session.HostMessage += _onHostMessage;

        ApplyScreen(session.CurrentScreen);
        ApplyStatus(session.KeyboardStatus);
        ApplyConnection(session.ConnectionState);
    }

    public SessionProfile Profile => _session.Profile;
    public string Title => $"{Profile.Name} - {Profile.Host}";

    private void ApplyScreen(ScreenSnapshot snapshot)
    {
        if (_disposed) return;
        Screen = snapshot;
        CursorText = StatusFormatter.Cursor(snapshot.Cursor);
    }

    private void ApplyStatus(KeyboardStatus status)
    {
        if (_disposed) return;
        KeyboardText = StatusFormatter.Keyboard(status);
        InsertText = StatusFormatter.Insert(status.InsertMode);
        ModelText = StatusFormatter.Model(Profile, status.LuName);
    }

    private void ApplyConnection(ConnectionState state)
    {
        if (_disposed) return;
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
        catch (Exception ex)
        {
            ErrorMessage = "Unexpected error: " + ex.Message;
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
        catch (Exception ex)
        {
            ErrorMessage = "Unexpected error: " + ex.Message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _session.ScreenUpdated -= _onScreenUpdated;
        _session.StatusChanged -= _onStatusChanged;
        _session.ConnectionChanged -= _onConnectionChanged;
        _session.Faulted -= _onFaulted;
        _session.HostMessage -= _onHostMessage;
        await _session.DisposeAsync();
    }
}
