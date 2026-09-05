using System.Collections.Concurrent;
using System.Text;
using LizTerm.Backend.B3270.Process;
using LizTerm.Backend.B3270.Protocol;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270;

public sealed class B3270Session : IEmulatorSession
{
    public static readonly Version MinimumVersion = new(4, 2, 0);

    private readonly Func<IB3270Process> _processFactory;
    private readonly WireLog? _wireLog;
    private readonly object _writeLock = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RunResultIndication>> _pending = new();
    private readonly ScreenBuffer _buffer = new(24, 80);
    private IB3270Process? _process;
    private Thread? _readerThread;
    private TaskCompletionSource<HelloIndication>? _hello;
    private int _tagCounter;
    private volatile bool _shuttingDown;
    private volatile TaskCompletionSource? _disconnected;
    private TransferContext? _transfer;

    /// <summary>How long <see cref="DisconnectAsync"/> waits for b3270 to report the connection closed
    /// after accepting the action. Tests shorten it.</summary>
    internal TimeSpan DisconnectTimeout { get; set; } = TimeSpan.FromSeconds(5);
    private bool _wireLogWarningRaised;

    public B3270Session(SessionProfile profile, Func<IB3270Process> processFactory, WireLog? wireLog = null)
    {
        Profile = profile;
        _processFactory = processFactory;
        _wireLog = wireLog;
        CurrentScreen = _buffer.Snapshot();
    }

    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public SessionProfile Profile { get; }
    public ScreenSnapshot CurrentScreen { get; private set; }
    public ConnectionState ConnectionState { get; private set; } = ConnectionState.Disconnected;
    public TlsInfo? Tls { get; private set; }
    public KeyboardStatus KeyboardStatus { get; private set; } = KeyboardStatus.Initial;

    public event EventHandler<ScreenSnapshot>? ScreenUpdated;
    public event EventHandler<KeyboardStatus>? StatusChanged;
    public event EventHandler<ConnectionState>? ConnectionChanged;
    public event EventHandler<BackendFault>? Faulted;
    public event EventHandler<string>? HostMessage;

    public static IReadOnlyList<string> BuildArguments(SessionProfile profile) =>
        ["-json", "-utf8", "-model", HostStringBuilder.ModelArgument(profile), "-codepage", profile.CodePage];

    // ---- lifecycle ----

    internal async Task StartProcessAsync(CancellationToken cancellationToken)
    {
        if (_process is not null) return;
        var process = _processFactory();
        var helloSource = new TaskCompletionSource<HelloIndication>(TaskCreationOptions.RunContinuationsAsynchronously);
        _hello = helloSource;
        _process = process;
        process.Start(BuildArguments(Profile));
        _readerThread = new Thread(ReadLoop) { IsBackground = true, Name = "b3270-reader" };
        _readerThread.Start();

        HelloIndication hello;
        try
        {
            hello = await helloSource.Task.WaitAsync(StartupTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            TearDown();
            throw new BackendUnavailableException($"b3270 did not answer within {StartupTimeout.TotalSeconds:0} seconds. stderr: {string.Join(" | ", process.StderrTail)}");
        }
        catch (BackendUnavailableException)
        {
            TearDown();
            throw;
        }

        if (!Version.TryParse(hello.Version, out var version) || version < MinimumVersion)
        {
            TearDown();
            throw new BackendUnavailableException($"b3270 version {hello.Version} is too old; {MinimumVersion} or newer is required.");
        }

        if (_wireLog is null && WireLog.LastOpenError is { } wireLogError && !_wireLogWarningRaised)
        {
            _wireLogWarningRaised = true;
            HostMessage?.Invoke(this, "Wire log disabled: " + wireLogError);
        }
    }

    private void ReadLoop()
    {
        var process = _process!;
        try
        {
            while (process.StandardOutput.ReadLine() is { } line)
            {
                _wireLog?.Inbound(line);
                if (!IndicationParser.TryParse(line, out var indication)) continue;
                try { Handle(indication); }
                catch (Exception ex) { HostMessage?.Invoke(this, "Internal error handling emulator output: " + ex.Message); }
            }
        }
        catch (Exception ex) when (_shuttingDown || !ReferenceEquals(process, _process) || ex is ObjectDisposedException or IOException)
        {
            // Stream closed during shutdown, or by TearDown after the process was already replaced.
        }
        OnProcessEnded(process);
    }

    private void OnProcessEnded(IB3270Process process)
    {
        // This runs on the raw reader thread with no surrounding try/catch (see ReadLoop), so
        // nothing here may throw: an uncaught exception on a background thread terminates the
        // process. WaitForExitAsync() can already be faulted (e.g. the process was disposed by
        // TearDown/DisposeAsync before the reader thread noticed EOF), and Faulted/ConnectionChanged
        // are external event handlers we don't control.
        //
        // TearDown clears _process before killing the old one, so a torn-down process arrives here
        // after the slot has moved on and must not touch the pending runs, hello, or fault state
        // that belong to its successor.
        if (!ReferenceEquals(process, _process)) return;

        int? exitCode = null;
        try
        {
            var exitTask = process.WaitForExitAsync();
            if (exitTask.Wait(TimeSpan.FromSeconds(2))) exitCode = exitTask.Result;
        }
        catch (Exception)
        {
            exitCode = null;
        }

        try
        {
            var fault = new BackendFault("The emulator engine (b3270) exited unexpectedly.", process.StderrTail, exitCode);
            foreach (var tag in _pending.Keys.ToArray())
                if (_pending.TryRemove(tag, out var tcs))
                    tcs.TrySetException(new BackendUnavailableException(fault.Message));
            _hello?.TrySetException(new BackendUnavailableException(fault.Message + " stderr: " + string.Join(" | ", process.StderrTail)));

            SetConnectionState(ConnectionState.Disconnected);
            if (!_shuttingDown)
            {
                Faulted?.Invoke(this, fault);
                // Let a later ConnectAsync spawn a fresh process through the factory instead of
                // being stuck thinking the dead one is still usable.
                process.Dispose();
                _process = null;
                _hello = null;
            }
        }
        catch (Exception)
        {
            // Swallow: an exception here must not escape the reader thread.
        }
    }

    /// <summary>Drops a process that failed to start. Only <see cref="DisposeAsync"/> sets <c>_shuttingDown</c>:
    /// that flag is for the session's life, and a start that fails must leave the next attempt able to report
    /// its own faults. The reader thread of the old process tells it apart by identity instead.</summary>
    private void TearDown()
    {
        var process = _process;
        _process = null;
        _hello = null;
        process?.Kill();
        process?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        // Dispose the wire log unconditionally: a fault (see OnProcessEnded) may already have
        // cleared _process, but the log still needs to be closed.
        _wireLog?.Dispose();
        if (_process is null) return;
        _shuttingDown = true;
        try
        {
            WriteLine(RunOperation.Serialize("quit", [new B3270Action("Quit")]));
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            _process.Kill();
        }
        _process.Dispose();
        _process = null;
    }

    // ---- running actions ----

    internal Task<RunResultIndication> RunAsync(params B3270Action[] actions) => RunAsync(actions, throwOnFailure: true);

    internal Task<RunResultIndication> RunRawAsync(IReadOnlyList<B3270Action> actions) => RunAsync(actions, throwOnFailure: false);

    internal int PendingCount => _pending.Count;

    private async Task<RunResultIndication> RunAsync(IReadOnlyList<B3270Action> actions, bool throwOnFailure)
    {
        if (_process is null) throw new InvalidOperationException("The session has not been started.");
        var tag = Interlocked.Increment(ref _tagCounter).ToString();
        var tcs = new TaskCompletionSource<RunResultIndication>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[tag] = tcs;
        try
        {
            WriteLine(RunOperation.Serialize(tag, actions));
        }
        catch
        {
            _pending.TryRemove(tag, out _);
            throw;
        }
        var result = await tcs.Task;
        if (throwOnFailure && !result.Success)
            throw new EmulatorActionException(string.Join("\n", result.Text));
        return result;
    }

    private void WriteLine(string line)
    {
        var process = _process ?? throw new InvalidOperationException("The session has not been started.");
        lock (_writeLock)
        {
            process.StandardInput.Write(line);
            process.StandardInput.Write('\n');
            process.StandardInput.Flush();
        }
        _wireLog?.Outbound(line);
    }

    // ---- indications ----

    private void Handle(Indication indication)
    {
        switch (indication)
        {
            case InitializeIndication init:
                foreach (var item in init.Items) Handle(item);
                break;
            case HelloIndication hello:
                _hello?.TrySetResult(hello);
                break;
            case RunResultIndication result when result.Tag is not null && _pending.TryRemove(result.Tag, out var tcs):
                tcs.TrySetResult(result);
                break;
            case UiErrorIndication error:
                HostMessage?.Invoke(this, "Protocol error: " + error.Text);
                break;
            default:
                HandleStateIndication(indication);
                break;
        }
    }

    private static readonly Dictionary<string, ConnectionState> ConnectionStates = new(StringComparer.Ordinal)
    {
        ["not-connected"] = ConnectionState.Disconnected,
        ["reconnecting"] = ConnectionState.Reconnecting,
        ["resolving"] = ConnectionState.Resolving,
        ["tcp-pending"] = ConnectionState.TcpPending,
        ["tls-pending"] = ConnectionState.TlsPending,
        ["tls-password-pending"] = ConnectionState.TlsPasswordPending,
        ["proxy-pending"] = ConnectionState.ProxyPending,
        ["telnet-pending"] = ConnectionState.TelnetPending,
        ["connected-nvt"] = ConnectionState.ConnectedNvt,
        ["connected-nvt-charmode"] = ConnectionState.ConnectedNvtCharMode,
        ["connected-3270"] = ConnectionState.Connected3270,
        ["connected-unbound"] = ConnectionState.ConnectedUnbound,
        ["connected-e-nvt"] = ConnectionState.ConnectedENvt,
        ["connected-sscp"] = ConnectionState.ConnectedSscp,
        ["connected-tn3270e"] = ConnectionState.ConnectedTn3270E,
    };

    private void HandleStateIndication(Indication indication)
    {
        switch (indication)
        {
            case ScreenModeIndication mode:
                _buffer.Resize(mode.Rows, mode.Columns, HostColor.Blue, HostColor.NeutralBlack);
                Publish();
                break;
            case EraseIndication erase:
                if (erase.LogicalRows is { } rows && erase.LogicalColumns is { } cols && (rows != _buffer.Rows || cols != _buffer.Columns))
                    _buffer.Resize(rows, cols, HostColor.NeutralWhite, HostColor.NeutralBlack);
                _buffer.Erase(
                    erase.Fg is null ? HostColor.NeutralWhite : ColorNames.ParseColor(erase.Fg),
                    erase.Bg is null ? HostColor.NeutralBlack : ColorNames.ParseColor(erase.Bg));
                Publish();
                break;
            case ScreenIndication screen:
                ApplyScreen(screen);
                Publish();
                break;
            case OiaIndication oia:
                ApplyOia(oia);
                break;
            case ConnectionIndication connection:
                var state = ConnectionStates.GetValueOrDefault(connection.State, ConnectionState.Disconnected);
                if (state == ConnectionState.Disconnected) Tls = null;
                SetConnectionState(state);
                break;
            case TlsIndication tls:
                Tls = new TlsInfo(tls.Secure, tls.Verified, tls.Session, tls.HostCert);
                break;
            case FtIndication { Bytes: { } bytes } when Volatile.Read(ref _transfer) is { } transfer:
                // Progress only. The outcome comes from the Transfer run's run-result, which carries the same
                // text as the "complete" indication; ft lines with no transfer in flight are dropped.
                transfer.Bytes = bytes;
                transfer.Progress?.Report(bytes);
                break;
            case PopupIndication popup:
                HostMessage?.Invoke(this, popup.Text);
                break;
        }
    }

    private void ApplyScreen(ScreenIndication screen)
    {
        if (screen.Rows is not null)
        {
            foreach (var row in screen.Rows)
            {
                var r = row.Row - 1;
                if (r < 0 || r >= _buffer.Rows) continue;
                foreach (var change in row.Changes)
                {
                    var c = change.Column - 1;
                    if (c < 0 || c >= _buffer.Columns) continue;
                    HostColor? fg = change.Fg is null ? null : ColorNames.ParseColor(change.Fg);
                    HostColor? bg = change.Bg is null ? null : ColorNames.ParseColor(change.Bg);
                    CellRendition? gr = change.Gr is null ? null : ColorNames.ParseRendition(change.Gr);
                    if (change.Text is not null)
                        _buffer.SetText(r, c, change.Text, fg, bg, gr);
                    else if (change.Count is { } count)
                        _buffer.SetAttributes(r, c, count, fg, bg, gr);
                }
            }
        }
        if (screen.Cursor is { } cursor)
        {
            // enabled:false hides the cursor but keeps its last position; enabled:true without a position re-shows it.
            var current = _buffer.Cursor;
            var row = cursor.Row is { } cr ? cr - 1 : current.Row;
            var column = cursor.Column is { } cc ? cc - 1 : current.Column;
            _buffer.SetCursor(new CursorPosition(row, column, cursor.Enabled));
        }
    }

    private void ApplyOia(OiaIndication oia)
    {
        var status = KeyboardStatus;
        switch (oia.Field)
        {
            case "lock":
                var (lockState, detail) = MapLock(oia.Value);
                status = status with { Lock = lockState, LockDetail = detail };
                break;
            case "insert":
                status = status with { InsertMode = oia.Value == "true" };
                break;
            case "typeahead":
                status = status with { Typeahead = oia.Value == "true" };
                break;
            case "lu":
                status = status with { LuName = oia.Value };
                break;
            default:
                return;
        }
        if (status == KeyboardStatus) return;
        KeyboardStatus = status;
        StatusChanged?.Invoke(this, status);
    }

    private static (KeyboardLock Lock, string? Detail) MapLock(string? value)
    {
        if (value is null) return (KeyboardLock.Unlocked, null);
        if (value.StartsWith("scrolled", StringComparison.Ordinal)) return (KeyboardLock.Scrolled, value);
        return value switch
        {
            "not-connected" => (KeyboardLock.NotConnected, null),
            "syswait" => (KeyboardLock.WaitingForHost, null),
            "twait" => (KeyboardLock.TerminalWait, null),
            "deferred" => (KeyboardLock.Deferred, null),
            "minus" => (KeyboardLock.MinusFunction, null),
            "oerr protected" => (KeyboardLock.ProtectedField, null),
            "oerr numeric" => (KeyboardLock.NumericOnly, null),
            "oerr overflow" => (KeyboardLock.Overflow, null),
            "oerr dbcs" => (KeyboardLock.Dbcs, null),
            "disabled" => (KeyboardLock.Disabled, null),
            "field" => (KeyboardLock.FieldWait, null),
            "file-transfer" => (KeyboardLock.FileTransfer, null),
            _ => (KeyboardLock.Unknown, value),
        };
    }

    private void Publish()
    {
        CurrentScreen = _buffer.Snapshot();
        ScreenUpdated?.Invoke(this, CurrentScreen);
    }

    private void SetConnectionState(ConnectionState state)
    {
        if (ConnectionState == state) return;
        ConnectionState = state;
        try
        {
            ConnectionChanged?.Invoke(this, state);
        }
        finally
        {
            if (state == ConnectionState.Disconnected) _disconnected?.TrySetResult();
        }
    }

    // ---- IEmulatorSession actions ----

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await StartProcessAsync(cancellationToken);
        await RunAsync(new B3270Action("Set", "verifyHostCert", Profile.VerifyCertificate ? "true" : "false"));
        var result = await RunRawAsync([new B3270Action("Connect", HostStringBuilder.Build(Profile))]);
        if (!result.Success) throw new ConnectionFailedException(result.Text);
    }

    /// <summary>Completes once b3270 has reported the connection closed (or the process has ended), not
    /// merely once it has accepted the Disconnect action, so callers can rely on the state afterwards.</summary>
    public async Task DisconnectAsync()
    {
        if (_process is null) return;
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _disconnected = disconnected;
        try
        {
            if (ConnectionState == ConnectionState.Disconnected) return;
            await RunRawAsync([new B3270Action("Disconnect")]);
            try
            {
                await disconnected.Task.WaitAsync(DisconnectTimeout);
            }
            catch (TimeoutException)
            {
                // b3270 accepted the action but never reported the state; the ConnectionChanged
                // event still fires if it does later. Hanging the caller would be worse.
            }
        }
        finally
        {
            _disconnected = null;
        }
    }

    public Task SendKeyAsync(TerminalKey key) => RunAsync(ActionMap.ForKey(key));

    /// <summary>x3270's String() interprets backslash escapes, so literal backslashes are doubled.</summary>
    public Task TypeTextAsync(string text) => RunAsync(new B3270Action("String", text.Replace("\\", "\\\\")));

    /// <summary>PasteString takes hexadecimal UTF-8, not literal text; it also applies b3270's
    /// margin-aware paste behavior by default, unlike String().</summary>
    public Task PasteTextAsync(string text) =>
        RunAsync(new B3270Action("PasteString", Convert.ToHexString(Encoding.UTF8.GetBytes(text))));

    public Task MoveCursorAsync(int row, int column) =>
        RunAsync(new B3270Action("MoveCursor", row.ToString(), column.ToString()));

    // ---- file transfer ----

    /// <summary>True while a Transfer run is pending. Tests use it to check the slot is freed.</summary>
    internal bool IsTransferInProgress => _transfer is not null;

    public async Task<FileTransferResult> TransferAsync(FileTransferRequest request, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_process is null) throw new InvalidOperationException("The session has not been started.");
        var context = new TransferContext(progress);
        if (Interlocked.CompareExchange(ref _transfer, context, null) is not null)
            throw new InvalidOperationException("A file transfer is already in progress.");
        try
        {
            // b3270 does not answer the Transfer run until the transfer ends, so this run-result is the outcome.
            var run = RunRawAsync([TransferMapper.ToAction(request)]);
            using var registration = cancellationToken.Register(() => _ = Task.Run(TryCancelTransferAsync));
            var result = await run;
            if (!result.Success && cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException("The file transfer was cancelled.", cancellationToken);
            return new FileTransferResult(result.Success, string.Join("\n", result.Text), context.Bytes);
        }
        finally
        {
            Interlocked.CompareExchange(ref _transfer, null, context);
        }
    }

    /// <summary>Transfer(Cancel) is fire-and-forget: b3270 answers "No transfer pending." if the transfer already
    /// ended, and a dead engine faults the pending Transfer run on its own.</summary>
    private async Task TryCancelTransferAsync()
    {
        try
        {
            await RunRawAsync([TransferMapper.CancelAction]);
        }
        catch (Exception)
        {
        }
    }

    private sealed class TransferContext(IProgress<long>? progress)
    {
        public IProgress<long>? Progress { get; } = progress;
        public long Bytes;
    }
}
