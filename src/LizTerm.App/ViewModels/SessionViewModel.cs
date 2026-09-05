using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.Clipboard;
using LizTerm.App.Dialogs;
using LizTerm.App.Files;
using LizTerm.App.Status;
using LizTerm.Core.Profiles;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

public partial class SessionViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IEmulatorSession _session;
    private readonly Action<Action> _dispatch;
    private readonly ITextClipboard _clipboard;
    private readonly ICertificatePrompt? _certificatePrompt;
    private readonly Action<SessionProfile>? _saveProfile;
    private readonly IFolderOpener? _folderOpener;
    private bool? _verifyOverride;
    private readonly EventHandler<ScreenSnapshot> _onScreenUpdated;
    private readonly EventHandler<KeyboardStatus> _onStatusChanged;
    private readonly EventHandler<ConnectionState> _onConnectionChanged;
    private readonly EventHandler<BackendFault> _onFaulted;
    private readonly EventHandler<string> _onHostMessage;
    private bool _disposed;

    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long one connect attempt may take before it is cancelled. An instance property so tests can
    /// shorten it without racing each other on a static.</summary>
    public TimeSpan ConnectTimeout { get; set; } = DefaultConnectTimeout;

    private CancellationTokenSource? _connectCts;
    private bool _connectCancelledByUser;
    private ConnectionState _furthestState;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    private ScreenSnapshot? _screen;

    [ObservableProperty] private string _connectionText = "";
    [ObservableProperty] private string _tlsText = "";
    [ObservableProperty] private string _keyboardText = "";
    [ObservableProperty] private string _insertText = "";
    [ObservableProperty] private string _cursorText = "";
    [ObservableProperty] private string _modelText = "";
    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PasteCommand))]
    private bool _isConnected;

    /// <summary>The mouse selection, bound two-way to the screen control. Cleared here whenever input goes to the host.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    private ScreenRegion? _selection;

    /// <param name="dispatch">Marshals a callback onto the UI thread. Tests pass <c>a => a()</c>.</param>
    /// <param name="clipboard">Text clipboard; the app passes <see cref="AvaloniaTextClipboard"/>, tests a fake.</param>
    /// <param name="certificatePrompt">Asked on a certificate verification failure; null declines.</param>
    /// <param name="saveProfile">Persists the profile when the user chooses "Always allow"; null for ad hoc profiles.</param>
    /// <param name="folderOpener">Opens the wire log directory for Help &gt; Show Wire Logs; null for tests that don't cover it.</param>
    public SessionViewModel(IEmulatorSession session, Action<Action> dispatch, ITextClipboard clipboard,
        ICertificatePrompt? certificatePrompt = null, Action<SessionProfile>? saveProfile = null,
        IFolderOpener? folderOpener = null)
    {
        _session = session;
        _dispatch = dispatch;
        _clipboard = clipboard;
        _certificatePrompt = certificatePrompt;
        _saveProfile = saveProfile;
        _folderOpener = folderOpener;

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

        _isWireLogging = session.WireLogPath is not null;
        WireLogText = StatusFormatter.WireLog(_isWireLogging);
    }

    public SessionProfile Profile => _session.Profile;
    public string Title => $"{Profile.Name} - {Profile.Host}";
    public EngineInfo Engine => _session.Engine;

    /// <summary>Where new wire logs go; the app uses the per-OS logs folder, tests a temp directory.</summary>
    public string WireLogDirectory { get; set; } = AppPaths.LogsDirectory();

    /// <summary>Help &gt; Wire Log. On starts a new timestamped log for this session; off closes it.</summary>
    [ObservableProperty] private bool _isWireLogging;

    [ObservableProperty] private string _wireLogText = "";

    public static string WireLogFileName(string profileName, DateTime now)
    {
        var safe = new string(profileName.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').ToArray());
        return $"wire-{safe}-{now:yyyyMMdd-HHmmss}.log";
    }

    partial void OnIsWireLoggingChanged(bool value)
    {
        var active = _session.WireLogPath is not null;
        if (value && !active)
        {
            try
            {
                Directory.CreateDirectory(WireLogDirectory);
                _session.StartWireLog(Path.Combine(WireLogDirectory, WireLogFileName(Profile.Name, DateTime.Now)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                or ArgumentException or NotSupportedException)
            {
                ErrorMessage = "Could not open the wire log: " + ex.Message;
                // Correcting the property from inside its own change notification is invisible to the menu's
                // two-way binding, which is still writing target to source: the item would keep a check mark for
                // a log that never started, and the next click would write the value the property already holds
                // and be swallowed. Marshalling puts the correction after that write, where it is honoured.
                _dispatch(() => IsWireLogging = false);
                return;
            }
        }
        else if (!value && active)
        {
            _session.StopWireLog();
        }
        WireLogText = StatusFormatter.WireLog(_session.WireLogPath is not null);
    }

    /// <summary>Help &gt; Show Wire Logs. Opens the wire log directory in the OS file manager, falling back to
    /// naming the path in the error banner when the platform cannot open it.</summary>
    [RelayCommand]
    private async Task ShowWireLogsAsync()
    {
        var directory = WireLogDirectory;
        try
        {
            Directory.CreateDirectory(directory);
            if (_folderOpener is not null && await _folderOpener.OpenAsync(directory)) return;
        }
        catch (Exception)
        {
            // Fall through to naming the path.
        }
        ErrorMessage = $"Could not open the logs folder. Wire logs are in {directory}.";
    }

    /// <summary>The last request a File Transfer dialog started from this window, so the next dialog opens as the
    /// user left it. Lives as long as the window; nothing is saved to the profile.</summary>
    public FileTransferRequest? LastTransferRequest { get; private set; }

    /// <summary>Builds the File Transfer dialog's view model around this session, pre-filled from
    /// <see cref="LastTransferRequest"/>. The window passes a picker over the dialog; tests pass a fake.</summary>
    public FileTransferViewModel CreateTransfer(IFilePicker picker)
    {
        var transfer = new FileTransferViewModel(_session, picker, _dispatch, LastTransferRequest);
        transfer.Started += request =>
        {
            // A transfer types the IND$FILE command into the screen: host input, so the selection goes like it
            // does for every other path that sends input.
            Selection = null;
            LastTransferRequest = request;
        };
        return transfer;
    }

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
        if (state > _furthestState) _furthestState = state;
    }

    [RelayCommand]
    private Task ConnectAsync() => ConnectWithAsync(new ConnectOptions(_verifyOverride));

    private async Task ConnectWithAsync(ConnectOptions options)
    {
        ErrorMessage = null;
        _furthestState = ConnectionState.Disconnected;
        _connectCancelledByUser = false;
        ConnectionFailedException? certificateFailure = null;
        using (var cts = new CancellationTokenSource(ConnectTimeout))
        {
            _connectCts = cts;
            try
            {
                await _session.ConnectAsync(options, cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                if (_disposed) return;
                if (!_connectCancelledByUser)
                    ErrorMessage = StatusFormatter.ConnectTimeout(Profile, ConnectTimeout, _furthestState >= ConnectionState.TelnetPending);
            }
            catch (ConnectionFailedException ex) when (ex.CertificateVerificationFailed && options.VerifyCertificate != false)
            {
                // Only recorded here. C# does not route an exception thrown inside a catch clause to that try's
                // sibling clauses, so the prompt, the profile save and the retry run after the block instead.
                certificateFailure = ex;
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
            finally
            {
                if (ReferenceEquals(_connectCts, cts)) _connectCts = null;
            }
        }
        if (certificateFailure is not null && !_disposed) await OfferConnectAnywayAsync(certificateFailure);
    }

    /// <summary>Spec 5.3: ask once; on yes reconnect with verification off for this attempt, and with Remember also
    /// save the profile and keep the override for the window's life. The reconnect cannot fail on verification,
    /// so this never loops. Both the prompt (a modal window) and the save (a file write) can fail, and this runs
    /// after the connect's catch clauses rather than inside one, so those failures reach the error banner instead
    /// of faulting the command.</summary>
    private async Task OfferConnectAnywayAsync(ConnectionFailedException failure)
    {
        var reason = failure.Lines.Where(line => line != "Connection failed:").ToList();
        CertificateDecision decision;
        try
        {
            decision = _certificatePrompt is null
                ? CertificateDecision.Declined
                : await _certificatePrompt.AskAsync(Profile.Host, reason, _saveProfile is not null);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not ask about the certificate: " + ex.Message;
            return;
        }

        // The prompt is unbounded, so the window may have closed while it was open.
        if (_disposed) return;
        if (!decision.ConnectAnyway)
        {
            ErrorMessage = failure.Message;
            return;
        }

        string? saveError = null;
        if (decision.Remember)
        {
            _verifyOverride = false;
            try
            {
                _saveProfile?.Invoke(Profile with { VerifyCertificate = false });
            }
            catch (Exception ex)
            {
                // The choice still holds for this window; only writing it back failed, so the connect goes ahead.
                saveError = "Could not save the profile: " + ex.Message;
            }
        }

        await ConnectWithAsync(new ConnectOptions(VerifyCertificate: false));

        // ConnectWithAsync clears ErrorMessage on entry, so a save failure is reported after the retry.
        if (saveError is not null && !_disposed)
            ErrorMessage = ErrorMessage is null ? saveError : $"{ErrorMessage} {saveError}";
    }

    /// <summary>While a connect is pending this cancels it (the backend sends the Disconnect); otherwise it disconnects.</summary>
    [RelayCommand]
    private Task DisconnectAsync()
    {
        if (_connectCts is { } pending)
        {
            if (!pending.IsCancellationRequested) _connectCancelledByUser = true;
            pending.Cancel();
            return Task.CompletedTask;
        }
        return Guard(_session.DisconnectAsync());
    }

    [RelayCommand]
    private Task SendKeyAsync(TerminalKey key)
    {
        Selection = null;
        return Guard(_session.SendKeyAsync(key));
    }

    [RelayCommand]
    private void DismissError() => ErrorMessage = null;

    public Task TypeTextAsync(string text)
    {
        Selection = null;
        return Guard(_session.TypeTextAsync(text));
    }

    public Task MoveCursorAsync(int row, int column)
    {
        Selection = null;
        return Guard(_session.MoveCursorAsync(row, column));
    }

    private bool CanCopy => Selection is not null && Screen is not null;

    /// <summary>Copies the selection as trimmed lines. Copying is not host input, so the selection stays.</summary>
    [RelayCommand(CanExecute = nameof(CanCopy))]
    private async Task CopyAsync()
    {
        if (Selection is not { } region || Screen is not { } screen) return;
        try
        {
            await _clipboard.SetTextAsync(screen.GetText(region));
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not copy: " + ex.Message;
        }
    }

    /// <summary>One margin-aware paste of the clipboard text. b3270 moves to the next row at the paste margin on '\n'.</summary>
    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task PasteAsync()
    {
        // Hotkeys execute commands without consulting CanExecute, so the guard lives here too.
        if (!IsConnected) return;
        string? text;
        try
        {
            text = await _clipboard.GetTextAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not read the clipboard: " + ex.Message;
            return;
        }
        if (string.IsNullOrEmpty(text)) return;
        Selection = null;
        await Guard(_session.PasteTextAsync(text.Replace("\r\n", "\n").Replace('\r', '\n')));
    }

    private bool CanSelectAll => Screen is not null;

    [RelayCommand(CanExecute = nameof(CanSelectAll))]
    private void SelectAll()
    {
        if (Screen is { } screen) Selection = ScreenRegion.Full(screen.Rows, screen.Columns);
    }

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
        _connectCts?.Cancel();
        _session.ScreenUpdated -= _onScreenUpdated;
        _session.StatusChanged -= _onStatusChanged;
        _session.ConnectionChanged -= _onConnectionChanged;
        _session.Faulted -= _onFaulted;
        _session.HostMessage -= _onHostMessage;
        await _session.DisposeAsync();
    }
}
