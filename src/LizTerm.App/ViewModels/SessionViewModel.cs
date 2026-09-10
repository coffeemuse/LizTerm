// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.Capture;
using LizTerm.App.Clipboard;
using LizTerm.App.Dialogs;
using LizTerm.App.Files;
using LizTerm.App.Rendering;
using LizTerm.App.Status;
using LizTerm.Core.Profiles;
using LizTerm.Core.Screen;
using LizTerm.Core.Security;
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
    private readonly ICertificateFetcher? _certificateFetcher;
    private readonly Func<SessionProfile, Task>? _saveAsProfile;
    /// <summary>The pin chosen in this window. The session's profile is fixed at construction, so a pin made after
    /// the window opened travels as a one-shot option on every later connect from here (spec 5.3).</summary>
    private CertificatePin? _pinOverride;
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
    private bool _socketOpened;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyScreenAsHtmlCommand))]
    [NotifyPropertyChangedFor(nameof(CanCopy))]
    [NotifyPropertyChangedFor(nameof(CanSelectAll))]
    [NotifyPropertyChangedFor(nameof(CanCaptureScreen))]
    [NotifyPropertyChangedFor(nameof(CanFind))]
    private ScreenSnapshot? _screen;

    /// <summary>Which crosshair lines follow the cursor, for this window only. Not a profile field: it is a
    /// display preference, and a home for those is #19's job rather than something to invent here (spec 4.3).
    /// </summary>
    [ObservableProperty] private CrosshairMode _crosshair;

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

    /// <summary>The connection state b3270 last reported, verbatim: what the two command guards are gated on
    /// (spec 6.4). Named Connection rather than ConnectionState because an [ObservableProperty] cannot take the
    /// name of the enum type it is declared with. Deriving bools from it — HasSocket, IsReconnecting — is what
    /// let the guards drift out of step with the engine through Resolving and TcpPending, which an
    /// engine-driven reconnect passes through with no command running.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private ConnectionState _connection;

    /// <summary>A connect attempt is in flight. Kept beside _connectCts, which is a plain field and raises
    /// nothing when assigned. Not ConnectCommand.IsRunning: that stays true through the certificate prompt and
    /// the profile save, which deliberately run after the connect's catch clauses.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private bool _connectPending;

    /// <summary>The mouse selection, bound two-way to the screen control. Cleared here whenever input goes to the host.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    [NotifyPropertyChangedFor(nameof(CanCopy))]
    private ScreenRegion? _selection;

    /// <param name="dispatch">Marshals a callback onto the UI thread. Tests pass <c>a => a()</c>.</param>
    /// <param name="clipboard">Text clipboard; the app passes <see cref="AvaloniaTextClipboard"/>, tests a fake.</param>
    /// <param name="certificatePrompt">Asked on a certificate verification failure; null declines.</param>
    /// <param name="saveProfile">Persists the profile when the user pins its certificate; null for ad hoc profiles.</param>
    /// <param name="folderOpener">Opens the wire log directory for Help &gt; Show Wire Logs; null for tests that don't cover it.</param>
    /// <param name="certificateFetcher">Reads what a TLS host presented so the prompt can show and pin it; null shows the prompt without a fingerprint.</param>
    /// <param name="saveAsProfile">Turns this session into a saved profile — the app opens the profile editor
    /// pre-filled and writes the result; null disables the menu item.</param>
    public SessionViewModel(IEmulatorSession session, Action<Action> dispatch, ITextClipboard clipboard,
        ICertificatePrompt? certificatePrompt = null, Action<SessionProfile>? saveProfile = null,
        IFolderOpener? folderOpener = null, ICertificateFetcher? certificateFetcher = null,
        Func<SessionProfile, Task>? saveAsProfile = null)
    {
        _session = session;
        _dispatch = dispatch;
        _clipboard = clipboard;
        _certificatePrompt = certificatePrompt;
        _saveProfile = saveProfile;
        _folderOpener = folderOpener;
        _certificateFetcher = certificateFetcher;
        _saveAsProfile = saveAsProfile;

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

        Find = new FindViewModel(MoveCursorAsync);

        ApplyScreen(session.CurrentScreen);
        ApplyStatus(session.KeyboardStatus);
        ApplyConnection(session.ConnectionState);

        _isWireLogging = session.WireLogPath is not null;
        WireLogText = StatusFormatter.WireLog(_isWireLogging);
    }

    public SessionProfile Profile => _session.Profile;
    public string Title => $"{Profile.Name} - {Profile.Host}";
    public EngineInfo Engine => _session.Engine;

    /// <summary>Find state for this window. Its own view model: see FindViewModel's own summary.</summary>
    public FindViewModel Find { get; }

    /// <summary>There is a screen to search. Like CanCaptureScreen, deliberately not IsConnected — find reads
    /// the snapshot and never needs an engine.</summary>
    public bool CanFind => Screen is not null;

    /// <summary>Where new wire logs go; the app uses the per-OS logs folder, tests a temp directory.</summary>
    public string WireLogDirectory { get; set; } = AppPaths.LogsDirectory();

    /// <summary>Help &gt; Wire Log. On starts a new timestamped log for this session; off closes it.</summary>
    [ObservableProperty] private bool _isWireLogging;

    [ObservableProperty] private string _wireLogText = "";

    public static string WireLogFileName(string profileName, DateTime now) =>
        $"wire-{SafeFileName.Of(profileName)}-{now:yyyyMMdd-HHmmss}.log";

    /// <summary>The name the Save dialog opens on. Same shape as a wire log's, so the two files a user might
    /// keep from one session sort together.</summary>
    public static string ScreenFileName(string profileName, DateTime now, string extension) =>
        $"screen-{SafeFileName.Of(profileName)}-{now:yyyyMMdd-HHmmss}.{extension}";

    /// <summary>The path for a new file of that name, or the first of name-2, name-3, ... that does not exist yet.
    /// Two logs started in the same second must not share a file (spec 8).</summary>
    internal static string UniquePath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path)) return path;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(directory, $"{stem}-{n}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    partial void OnIsWireLoggingChanged(bool value)
    {
        var active = _session.WireLogPath is not null;
        if (value && !active)
        {
            try
            {
                Directory.CreateDirectory(WireLogDirectory);
                _session.StartWireLog(UniquePath(WireLogDirectory, WireLogFileName(Profile.Name, DateTime.Now)));
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
        Find.OnScreen(snapshot);
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
        Connection = state;
        ConnectionText = StatusFormatter.Connection(state, Profile.Host);
        TlsText = StatusFormatter.Tls(_session.Tls);
        if (state.HasSocket()) _socketOpened = true;
    }

    /// <summary>Offered only while the engine is fully idle: any state but Disconnected means an attempt is
    /// already under way or a session is up, and a second attempt over one already running is not something the
    /// app can honour. x3270's own File menu disables it too. ConnectPending is named as well because the state
    /// is still Disconnected through the first moments of a manual connect. Gating on the raw state rather than
    /// on HasSocket is spec 6.4's rule and matters because an engine-driven reconnect cycles through Resolving
    /// and TcpPending with no command running to disable this one.</summary>
    public bool CanConnect => !ConnectPending && Connection == ConnectionState.Disconnected;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private Task ConnectAsync() => ConnectWithAsync(new ConnectOptions(Pin: _pinOverride));

    /// <returns>True when this attempt itself connected; false after any failure, including one the certificate
    /// prompt then turned into a further attempt.</returns>
    private async Task<bool> ConnectWithAsync(ConnectOptions options)
    {
        ErrorMessage = null;
        _socketOpened = false;
        _connectCancelledByUser = false;
        ConnectionFailedException? certificateFailure = null;
        var connected = false;
        using (var cts = new CancellationTokenSource(ConnectTimeout))
        {
            _connectCts = cts;
            ConnectPending = true;
            try
            {
                await _session.ConnectAsync(options, cts.Token);
                connected = true;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                if (_disposed) return false;
                if (!_connectCancelledByUser)
                    ErrorMessage = StatusFormatter.ConnectTimeout(Profile, ConnectTimeout, _socketOpened);
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
                ConnectPending = false;
            }
        }
        if (certificateFailure is not null && !_disposed) await OfferConnectAnywayAsync(certificateFailure);
        return connected;
    }

    /// <summary>Spec 5.3. Reads what the host presented (TLS profiles only), asks once, and then either connects
    /// without verification for this attempt only or pins the certificate: the retry verifies against the pin, and
    /// only once the engine has accepted it is the profile saved with the pin and verification on — for an ad hoc
    /// session there is nothing to save it to, and the pin holds for this window alone until Save as Profile — after
    /// which every later connect from this window passes the same pin. A pin the engine rejects on that retry is not offered
    /// again (the request says so), so this never loops, and it is not kept either, so the next prompt can offer
    /// Remember afresh. The prompt (a modal window), the fetch (a
    /// socket), and the save (a file write) can all fail, and this runs after the connect's catch clauses rather
    /// than inside one, so those failures reach the error banner instead of faulting the command.</summary>
    private async Task OfferConnectAnywayAsync(ConnectionFailedException failure)
    {
        var reason = failure.Lines.Where(line => line != "Connection failed:").ToList();
        var previous = _pinOverride ?? Profile.PinnedCertificate;

        PresentedCertificate? presented = null;
        string? fetchError = null;
        if (Profile.UseTls && _certificateFetcher is not null)
        {
            // The fetcher only speaks TLS-on-connect, which is what a TLS profile is; a plain profile the host upgraded
            // through STARTTLS gets the one-time allow without a fingerprint (spec 5.1). The connect's own token source
            // is gone by now, so the fetch gets a fresh one with the same bound.
            using var fetchCts = new CancellationTokenSource(ConnectTimeout);
            try
            {
                presented = await _certificateFetcher.FetchAsync(Profile.Host, Profile.Port, fetchCts.Token);
            }
            catch (Exception ex)
            {
                fetchError = ex.Message;
            }
            if (_disposed) return;
        }

        // Deliberately NOT "_saveProfile is not null && Profile.UseTls". A pin lives in _pinOverride for the
        // window's life whether or not there is a file behind this session, and gating the offer on having one
        // made Quick Connect's own headline case (#29: an ad hoc connection is the start of a profile) impossible
        // — an ad hoc TLS session got a bare Connect Anyway, no pin box, and not even a cannotPinReason to say
        // why, and File > Save as Profile then produced a profile that failed verification on every later connect.
        // What having a file changes is only whether the accepted pin is ALSO written back below.
        var tlsProfile = Profile.UseTls;
        // Spec item 6 (plan 3d task 8): reuse the session's own rule rather than recompute it — the same
        // CanPinCertificates a Windows/Schannel session already used to refuse a pinned connect loudly (spec 4) is
        // what must stop this prompt offering to pin one, or a user could check "Trust this certificate" believing
        // it protects them when the engine has no way to enforce it.
        var canPin = tlsProfile && _session.CanPinCertificates && presented is { Pinnable: true } &&
            !CertificateReader.SameFingerprint(presented.Sha256, previous?.Sha256);
        string? cannotPinReason = null;
        if (tlsProfile && !canPin && presented is not null)
        {
            cannotPinReason = !_session.CanPinCertificates
                ? "This engine cannot verify a pinned certificate; connecting anyway applies to this attempt only."
                : presented.Pinnable
                    ? "The engine rejected the pinned certificate; connecting anyway applies to this attempt only."
                    : $"This certificate cannot be pinned: {presented.NotPinnableReason}. Connect Anyway applies to this attempt only.";
        }
        var request = new CertificatePromptRequest(Profile.Host, reason, presented, fetchError, previous, canPin, cannotPinReason);

        CertificateDecision decision;
        try
        {
            decision = _certificatePrompt is null ? CertificateDecision.Declined : await _certificatePrompt.AskAsync(request);
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

        ConnectOptions retry;
        CertificatePin? pin = null;
        if (decision.Remember && canPin)
        {
            // Held for the retry, so a rejection is reported as the engine rejecting this pin; nothing is written
            // until the engine has accepted it, because the reader's verdict and OpenSSL's can differ (a weak key, a
            // SHA-1 signature, an unsuitable purpose), and a saved pin the engine refuses would dead-end every later
            // connect from the profile.
            pin = new CertificatePin(presented!.Sha256, presented.Subject, presented.Pem);
            _pinOverride = pin;
            retry = new ConnectOptions(Pin: pin);
        }
        else
        {
            retry = new ConnectOptions(VerifyCertificate: false);
        }

        var connected = await ConnectWithAsync(retry);

        if (pin is null || _disposed) return;
        if (!connected)
        {
            if (ReferenceEquals(_pinOverride, pin)) _pinOverride = null;
            return;
        }
        try
        {
            // Null for an ad hoc session: there is no file to write back to. The pin still holds for this window
            // through _pinOverride, and File > Save as Profile is what makes it permanent.
            _saveProfile?.Invoke(Profile with { PinnedCertificate = pin, VerifyCertificate = true });
        }
        catch (Exception ex)
        {
            // The choice still holds for this window; only writing it back failed.
            ErrorMessage = "Could not save the profile: " + ex.Message;
        }
    }

    /// <summary>The inverse of CanConnect on the raw state, plus a pending connect: Resolving, TcpPending and
    /// Reconnecting carry no socket yet, and those are exactly the moments a user wants to give up on an
    /// attempt.</summary>
    public bool CanDisconnect => ConnectPending || Connection != ConnectionState.Disconnected;

    /// <summary>While a connect is pending this cancels it (the backend sends the Disconnect); otherwise it disconnects.</summary>
    [RelayCommand(CanExecute = nameof(CanDisconnect))]
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

    /// <summary>Keys overlap by nature (auto-repeat, fast typing) and the backend serializes its writes, so the
    /// screen's keystrokes call this method directly, as they do <see cref="TypeTextAsync"/>; the command exists for
    /// the Keys menu, whose items may disable for the length of a round trip.</summary>
    [RelayCommand]
    public Task SendKeyAsync(TerminalKey key)
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

    /// <summary>Public because the native Edit menu binds IsEnabled to it directly: those items are driven by
    /// Click handlers rather than commands, so they get none of the greying a command's CanExecute gives the
    /// classic ones. Kept as the CopyCommand's CanExecute too, so the two menus cannot drift.</summary>
    public bool CanCopy => Selection is not null && Screen is not null;

    /// <summary>Copies the selection as trimmed lines. Copying is not host input, so the selection stays.</summary>
    [RelayCommand(CanExecute = nameof(CanCopy))]
    public async Task CopyAsync()
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
    public async Task PasteAsync()
    {
        // The screen's paste hotkey calls this method directly, so the guard lives here as well as in CanExecute.
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

    /// <inheritdoc cref="CanCopy"/>
    public bool CanSelectAll => Screen is not null;

    [RelayCommand(CanExecute = nameof(CanSelectAll))]
    public void SelectAll()
    {
        if (Screen is { } screen) Selection = ScreenRegion.Full(screen.Rows, screen.Columns);
    }

    /// <summary>There is a screen to capture. Deliberately not IsConnected: capture needs no engine, and the
    /// moment it is most wanted is often a session the host has just dropped (spec 3.1).</summary>
    public bool CanCaptureScreen => Screen is not null;

    /// <summary>What the Save dialog's own File Format popup offers. Text first, because it is what
    /// <see cref="ScreenFileName"/> suggests and the dialog opens on its first entry — a popup contradicting
    /// the filename beside it is worse than no popup. The extension still decides the format below; this only
    /// makes that choice visible and gets the extension appended, which typing ".html" by hand used to be the
    /// only route to.</summary>
    private static readonly IReadOnlyList<SaveFormat> ScreenFormats =
    [
        new("Plain text", "txt"),
        new("HTML", "html"),
    ];

    /// <summary>File &gt; Save Screen As... The format follows the extension the OS dialog returned; we write
    /// the bytes rather than handing a path to anything else, because the dialog has just made a promise about
    /// overwriting and only we can keep it.</summary>
    public async Task SaveScreenAsync(IFilePicker picker)
    {
        if (Screen is not { } screen) return;
        try
        {
            var suggested = ScreenFileName(Profile.Name, DateTime.Now, "txt");
            if (await picker.PickSaveLocationAsync(suggested, "Save screen as", ScreenFormats) is not { } path) return;

            var extension = Path.GetExtension(path);
            var html = extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
                       || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);
            await File.WriteAllTextAsync(path, html ? ScreenHtml.RenderDocument(screen) : screen.ToText());
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not save the screen: " + ex.Message;
        }
    }

    /// <summary>Edit &gt; Copy Screen as HTML. The cheapest useful capture and the one that reaches a bug
    /// report.</summary>
    [RelayCommand(CanExecute = nameof(CanCaptureScreen))]
    public async Task CopyScreenAsHtmlAsync()
    {
        if (Screen is not { } screen) return;
        try
        {
            await _clipboard.SetTextAsync(ScreenHtml.Render(screen));
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not copy the screen: " + ex.Message;
        }
    }

    public bool CanSaveAsProfile => _saveAsProfile is not null;

    /// <summary>File &gt; Save as Profile. Public because both menus drive it through Click handlers rather than a
    /// command, the way Save Screen As does — and so, like it, it carries no [RelayCommand]: the generated command
    /// would be bound by nothing, and anyone who later bound it would silently get the disables-while-running
    /// behaviour the Click handlers exist to avoid. CanSaveAsProfile is what the two menu items bind IsEnabled to.
    /// A pin taken in THIS window lives in _pinOverride rather than in
    /// the session's profile, which is fixed at construction, so it is folded in here — otherwise a certificate
    /// the user deliberately trusted during an ad hoc session would be dropped by the profile it becomes.</summary>
    public async Task SaveAsProfileAsync()
    {
        if (_saveAsProfile is null) return;
        try
        {
            await _saveAsProfile(_pinOverride is null
                ? Profile
                : Profile with { PinnedCertificate = _pinOverride, VerifyCertificate = true });
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not save the profile: " + ex.Message;
        }
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
