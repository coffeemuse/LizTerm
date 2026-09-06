using System.Collections.Concurrent;
using System.Text;
using LizTerm.Backend.B3270.Process;
using LizTerm.Backend.B3270.Protocol;
using LizTerm.Core.Screen;
using LizTerm.Core.Security;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270;

public sealed class B3270Session : IEmulatorSession
{
    public static readonly Version MinimumVersion = new(4, 2, 0);
    public const string CertificateFailurePrefix = "TLS: Host certificate verification failed";

    private readonly Func<IB3270Process> _processFactory;
    private WireLog? _wireLog;
    private readonly string? _wireLogError;
    private readonly B3270Location _location;
    private readonly object _writeLock = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RunResultIndication>> _pending = new();
    private readonly ScreenBuffer _buffer = new(24, 80);
    private IB3270Process? _process;
    private Thread? _readerThread;
    private TaskCompletionSource<HelloIndication>? _hello;
    private int _tagCounter;
    private volatile bool _shuttingDown;
    /// <summary>Completed while the connection is down and replaced by a fresh source when it comes up: owned by
    /// the connection state rather than by whoever waits, so a report cannot be consumed early, orphaned by a
    /// waiter that gave up, or missed by one that joined late (spec 8).</summary>
    private TaskCompletionSource _disconnected = CompletedSource();
    private TransferContext? _transfer;
    /// <summary>Why there is no process after there was one: set when the engine dies, cleared by the next start,
    /// so an action sent to a dead engine reports the fault rather than a session that was never started.</summary>
    private volatile BackendFault? _fault;

    /// <summary>How long <see cref="DisconnectAsync"/> waits for b3270 to report the connection closed
    /// after accepting the action. Tests shorten it.</summary>
    internal TimeSpan DisconnectTimeout { get; set; } = TimeSpan.FromSeconds(5);
    private bool _wireLogWarningRaised;

    /// <param name="wireLog">A log already open (from the environment), or null.</param>
    /// <param name="wireLogError">Why the environment's log could not be opened; reported once as a HostMessage.</param>
    /// <param name="location">The binary this session's processes run; null (tests) reads as unknown.</param>
    public B3270Session(SessionProfile profile, Func<IB3270Process> processFactory, WireLog? wireLog = null, string? wireLogError = null, B3270Location? location = null)
    {
        Profile = profile;
        _processFactory = processFactory;
        _wireLog = wireLog;
        _wireLogError = wireLogError;
        _location = location ?? B3270Location.Unknown;
        Engine = new EngineInfo("b3270", null, _location.Path, _location.Source);
        CurrentScreen = _buffer.Snapshot();
    }

    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Where the engine's trust anchors come from when no pin is in force. Defaults to none, which leaves
    /// the engine on whatever trust it was built with; the App injects the real store in SessionFactory. Defaulting
    /// to the machine's store would make every test that connects with a verifying profile depend on the roots that
    /// machine happens to hold.</summary>
    public ITrustAnchorSource TrustAnchors { get; init; } = NoTrustAnchors.Instance;

    public SessionProfile Profile { get; }
    public ScreenSnapshot CurrentScreen { get; private set; }
    public ConnectionState ConnectionState { get; private set; } = ConnectionState.Disconnected;
    public TlsInfo? Tls { get; private set; }
    public KeyboardStatus KeyboardStatus { get; private set; } = KeyboardStatus.Initial;
    public EngineInfo Engine { get; private set; }

    public string? WireLogPath => Volatile.Read(ref _wireLog)?.Path;

    public void StartWireLog(string path)
    {
        lock (_writeLock)
        {
            if (_wireLog is not null) throw new InvalidOperationException("A wire log is already active.");
            try
            {
                _wireLog = new WireLog(path);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or NotSupportedException or IOException)
            {
                // DirectoryNotFoundException (an IOException subclass), ArgumentException (e.g. an empty path),
                // and NotSupportedException (a malformed path) are all wrapped into the exact IOException type
                // so callers can catch one type for every unopenable path.
                throw new IOException(ex.Message, ex);
            }
        }
    }

    public void StopWireLog()
    {
        WireLog? old;
        lock (_writeLock)
        {
            old = _wireLog;
            _wireLog = null;
        }
        old?.Dispose();
    }

    public event EventHandler<ScreenSnapshot>? ScreenUpdated;
    public event EventHandler<KeyboardStatus>? StatusChanged;
    public event EventHandler<ConnectionState>? ConnectionChanged;
    public event EventHandler<BackendFault>? Faulted;
    public event EventHandler<string>? HostMessage;

    public static IReadOnlyList<string> BuildArguments(SessionProfile profile) =>
        ["-json", "-utf8", "-model", HostStringBuilder.ModelArgument(profile), "-codepage", profile.CodePage];

    // ---- lifecycle ----

    /// <summary>Whether a process slot is filled. Test seam, like <see cref="PendingCount"/>.</summary>
    internal bool HasProcess => _process is not null;

    internal async Task StartProcessAsync(CancellationToken cancellationToken)
    {
        // DisposeAsync clears the process slot, so without this a connect arriving afterwards — a modal dialog's
        // continuation outliving its window — would spawn an engine nothing owns and nothing will ever dispose.
        // ObjectDisposedException is an InvalidOperationException, so callers already catching that still do.
        ObjectDisposedException.ThrowIf(_shuttingDown, this);
        if (_process is not null) return;
        var process = _processFactory();
        var helloSource = new TaskCompletionSource<HelloIndication>(TaskCreationOptions.RunContinuationsAsynchronously);
        _hello = helloSource;
        _process = process;
        _fault = null;
        try
        {
            process.Start(BuildArguments(Profile));
        }
        catch
        {
            // The slot is already filled, so a Start that throws would otherwise make every later attempt
            // short-circuit on `_process is not null` and then trip over a process that was never started.
            TearDown();
            throw;
        }
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
        catch (OperationCanceledException)
        {
            TearDown();
            throw;
        }

        if (!Version.TryParse(hello.Version, out var version) || version < MinimumVersion)
        {
            TearDown();
            throw new BackendUnavailableException($"b3270 version {hello.Version} is too old; {MinimumVersion} or newer is required.");
        }

        Engine = Engine with { Version = $"{hello.Version} ({hello.Build})" };

        if (_wireLogError is { } wireLogError && !_wireLogWarningRaised)
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
                Volatile.Read(ref _wireLog)?.Inbound(line);
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
                _fault = fault;
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
    /// that flag is for the session's life — it closes the session to any later start as well as silencing the
    /// fault report — and a start that fails must leave the next attempt able to report its own faults. The
    /// reader thread of the old process tells it apart by identity instead.</summary>
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
        // Set before the slot is even read, so a session disposed without ever being started is still closed to
        // a later connect; it also stops OnProcessEnded reporting the shutdown it is about to cause as a fault.
        _shuttingDown = true;
        try
        {
            // Snapshot the slot: OnProcessEnded clears it from the reader thread after publishing Disconnected,
            // so re-reading the field here can hand back null between the check and the kill (as TearDown does).
            var process = _process;
            if (process is null) return;
            try
            {
                WriteLine(RunOperation.Serialize("quit", [new B3270Action("Quit")]));
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
                // OnProcessEnded may already have killed and disposed this process, so the kill can fail too.
                try { process.Kill(); }
                catch (Exception) { /* already gone; nothing left to stop */ }
            }
            try { process.Dispose(); }
            catch (Exception) { /* already disposed by OnProcessEnded */ }
            Interlocked.CompareExchange(ref _process, null, process);
        }
        finally
        {
            // Closed last, and on every path: a fault (see OnProcessEnded) may already have cleared _process, but
            // the log still needs closing — and the Quit, plus whatever b3270 says on its way out, are exactly
            // the lines a report about a hang on close turns on.
            StopWireLog();
        }
    }

    // ---- running actions ----

    internal Task<RunResultIndication> RunAsync(params B3270Action[] actions) => RunAsync(actions, throwOnFailure: true);

    /// <param name="timeout">A bound on b3270's answer, for runs that must not outlive their caller.</param>
    /// <param name="cancellationToken">Gives up on the answer, releasing the pending slot.</param>
    internal Task<RunResultIndication> RunRawAsync(IReadOnlyList<B3270Action> actions, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(actions, throwOnFailure: false, timeout, cancellationToken);

    internal int PendingCount => _pending.Count;

    /// <param name="timeout">Bounds the wait for b3270's answer. Most runs pass none: b3270 answers at once, and
    /// Transfer deliberately does not answer until the transfer has ended.</param>
    /// <param name="cancellationToken">Ends the wait when the caller's own attempt is over.</param>
    private async Task<RunResultIndication> RunAsync(IReadOnlyList<B3270Action> actions, bool throwOnFailure,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        RequireProcess();
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
        RunResultIndication result;
        try
        {
            var wait = tcs.Task;
            if (timeout is { } limit) wait = wait.WaitAsync(limit, cancellationToken);
            else if (cancellationToken.CanBeCanceled) wait = wait.WaitAsync(cancellationToken);
            result = await wait;
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            // Nothing will complete this tag now, and Handle drops a run-result whose tag is gone, so release
            // the slot instead of holding it for the session's life.
            _pending.TryRemove(tag, out _);
            throw;
        }

        if (throwOnFailure && !result.Success)
            throw new EmulatorActionException(string.Join("\n", result.Text));
        return result;
    }

    private void WriteLine(string line)
    {
        var process = RequireProcess();
        lock (_writeLock)
        {
            // Logged before the bytes go out, not after: stdin auto-flushes, so b3270 can answer the moment the
            // newline lands, and the reader thread logs inbound lines under the log's own lock rather than this
            // one. Logging afterwards let a run-result be written ahead of the run that provoked it, which is the
            // ordering a reader uses to attribute a failure. The cost is that a write which then throws leaves a
            // line in the log that never reached the engine — visible anyway, because nothing answers it.
            _wireLog?.Outbound(line);
            process.StandardInput.Write(line);
            process.StandardInput.Write('\n');
            process.StandardInput.Flush();
        }
    }

    /// <summary>The live process, or the reason there is none: the engine's last fault when it died, otherwise a
    /// session that was never started.</summary>
    private IB3270Process RequireProcess() =>
        _process ?? throw (_fault is { } fault
            ? new BackendUnavailableException(fault.Message)
            : new InvalidOperationException("The session has not been started."));

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
        // The new source is in place before the state leaves Disconnected, so a waiter never sees a connection that
        // is up beside a source that is already complete.
        if (ConnectionState == ConnectionState.Disconnected)
            Volatile.Write(ref _disconnected, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        ConnectionState = state;
        try
        {
            ConnectionChanged?.Invoke(this, state);
        }
        finally
        {
            if (state == ConnectionState.Disconnected) Volatile.Read(ref _disconnected).TrySetResult();
        }
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    // ---- IEmulatorSession actions ----

    public async Task ConnectAsync(ConnectOptions? options = null, CancellationToken cancellationToken = default)
    {
        await StartProcessAsync(cancellationToken);
        var verify = options?.VerifyCertificate ?? Profile.VerifyCertificate;
        // Spec 3.1: verification off means no pin; otherwise the one-shot pin wins over the profile's.
        var pin = verify ? options?.Pin ?? Profile.PinnedCertificate : null;
        // Off the caller's context: App.OpenSession starts every connect from the Avalonia UI thread, and nothing
        // in this codebase uses ConfigureAwait(false), so without this Task.Run the continuation after
        // StartProcessAsync — and so TrustAnchors.ExportPem() (measured 210 ms reading the OS store) and the 238 KB
        // synchronous write below — would run on that thread for every connect, TLS or not. DecideCaFile is the
        // whole decide-and-write step, so the CA file is fully written (or the source's short-circuit fully
        // resolved) before the thread pool hop back to the caller.
        var (caFile, anyName) = await Task.Run(() => DecideCaFile(pin, verify));
        LastCaFile = caFile;
        try
        {
            // Bounded by the caller's token: b3270 answers Set at once, but a wedged engine must not hold the attempt
            // open before the Connect has even gone out. All three values are sent every time so an attempt never
            // inherits the previous one's trust settings (spec 2).
            await RunAsync([TlsSettings(verify, caFile, anyName)], throwOnFailure: true, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await ConnectCoreAsync(cancellationToken);
        }
        finally
        {
            if (caFile is not null) TryDeleteCaFile(caFile);
        }
    }

    /// <summary>The trust decision for one connect attempt, and the file that carries it: spec 3, verification off
    /// means no CA file at all; otherwise a pin is the whole trust store, and without one the machine's own anchors
    /// are, because a statically linked engine has none it can use (spec 1). Evaluated off the caller's context (see
    /// <see cref="ConnectAsync"/>) since it is the only part of a connect that does real work: reading the trust
    /// source and writing its PEM to disk.</summary>
    private (string? CaFile, bool AnyName) DecideCaFile(CertificatePin? pin, bool verify)
    {
        // Only reached when there is no pin (pin?.Pem is null iff pin is null: Pem is non-nullable), which is what
        // keeps a pinned connect from ever calling into the trust-anchor source at all.
        var anchors = pin is null && verify ? TrustAnchors.ExportPem() : null;
        // A source with nothing to offer, or only whitespace, leaves caFile empty: an empty *file* fails the
        // connect outright, so "nothing usable" has to collapse to null before it reaches WriteCaFile. Deliberately
        // not applied to pin.Pem: a pin with an empty PEM is a broken pin and must keep failing loudly (b3270
        // rejects the resulting empty caFile) rather than silently falling back to wider trust.
        var pem = pin?.Pem ?? (string.IsNullOrWhiteSpace(anchors) ? null : anchors);
        // The engine loads caFile when it builds the TLS context for this connection, inside the Connect run, so
        // the file has to outlive that run and nothing more.
        var caFile = pem is null ? null : WriteCaFile(pem, pin is null ? "roots" : "pin");
        // A pin that is one self-signed certificate names the host by itself, so the name check adds nothing. A pin
        // that also carries CA certificates makes each of them a trust anchor (OpenSSL trusts every member of caFile),
        // and only the engine's normal name check then keeps a certificate that CA issued for another host from
        // verifying here.
        var anyName = pin is not null && CertificateReader.CountCertificates(pin.Pem) == 1;
        return (caFile, anyName);
    }

    /// <summary>The Set action carrying one attempt's trust settings (spec 4.1). <paramref name="acceptAnyName"/>
    /// turns the engine's host-name check off, which is right only for a pin that is a single self-signed
    /// certificate; an empty acceptHostname is the engine's normal check against the connect host. An empty
    /// caFile leaves the engine on its own default trust.</summary>
    internal static B3270Action TlsSettings(bool verify, string? caFile, bool acceptAnyName) =>
        new("Set", "verifyHostCert", verify ? "true" : "false", "caFile", caFile ?? "", "acceptHostname", caFile is not null && acceptAnyName ? "any" : "");

    /// <summary>The path of the last CA file written — a pin or the trust anchors — deleted or not. Test seam.</summary>
    internal string? LastCaFile { get; private set; }

    /// <param name="kind">"pin" or "roots": the file name says which of the two callers wrote it, which is what a
    /// leftover in the temp directory and a failing test both have to be read by.</param>
    internal static string WriteCaFile(string pem, string kind)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lizterm-{kind}-{Guid.NewGuid():N}.pem");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        // Owner-only on Unix; the Windows temp directory is already per user.
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            using var stream = new FileStream(path, options);
            using var writer = new StreamWriter(stream);
            writer.Write(pem);
        }
        catch
        {
            // A write that fails partway (disk full, etc.) never hands the path back to the caller, so the
            // caller's finally can never delete it; clean up the partial file here instead of leaking it.
            TryDeleteCaFile(path);
            throw;
        }
        return path;
    }

    private static void TryDeleteCaFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // A leftover temp file holds a public certificate; there is nothing better to do about it here.
        }
    }

    private async Task ConnectCoreAsync(CancellationToken cancellationToken)
    {
        // b3270 answers a Disconnect while a Connect is pending (verified against 4.5ga6): the Disconnect run
        // succeeds at once and the Connect run then fails with "Connection failed", which is the cancel's own
        // consequence rather than an error to report. Its own token lets that wait be given up on if the engine
        // never says so, which releases the pending slot rather than abandoning it.
        using var runCts = new CancellationTokenSource();
        var run = RunRawAsync([new B3270Action("Connect", HostStringBuilder.Build(Profile))], cancellationToken: runCts.Token);
        Task? disconnect = null;
        RunResultIndication? result = null;
        try
        {
            using (cancellationToken.Register(() =>
            {
                Volatile.Write(ref disconnect, Task.Run(TryDisconnectQuietlyAsync));
                // The Disconnect is what makes b3270 fail the pending Connect run, so allow that long for it to
                // arrive and no longer: a wedged engine must not hold the attempt open past its cancellation.
                runCts.CancelAfter(DisconnectTimeout);
            }))
                result = await run;
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested)
        {
            // The engine never failed the Connect run after the cancel. The cancellation below is the outcome.
        }
        finally
        {
            // The registration is disposed by now, so the slot is final. Awaiting here rather than only on the
            // cancelled path below is what keeps that promise when the Connect run faults instead of returning —
            // the engine dying mid-cancel — which would otherwise let the Disconnect land on the next attempt.
            // TransferAsync holds its own cancel the same way.
            if (Volatile.Read(ref disconnect) is { } started) await started;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            // A cancel that raced the outcome still wins: the run may even have succeeded, so make sure a Disconnect
            // went out and was answered before reporting the cancellation.
            if (Volatile.Read(ref disconnect) is null) await TryDisconnectQuietlyAsync();
            // b3270 answers the Connect run before it reports the connection closed; wait for that report so the
            // session is actually reusable by the time the caller sees the exception (spec 4.1).
            await WaitForDisconnectedAsync();
            throw new OperationCanceledException(cancellationToken);
        }

        // Not cancelled, so the run above answered.
        var outcome = result!;
        if (outcome.Success) return;
        // Same lag as above: the failing run-result arrives before b3270's own not-connected indication.
        await WaitForDisconnectedAsync();
        var certificate = outcome.Text.Any(line => line.StartsWith(CertificateFailurePrefix, StringComparison.Ordinal));
        throw new ConnectionFailedException(outcome.Text, certificate);
    }

    private async Task TryDisconnectQuietlyAsync()
    {
        try
        {
            await RunRawAsync([new B3270Action("Disconnect")], DisconnectTimeout);
        }
        catch (Exception)
        {
            // The process may be gone, or the engine may accept the Disconnect and never answer it. Neither can
            // be allowed to hold the cancel open: the pending Connect run faults or is abandoned alongside it.
        }
    }

    /// <summary>Completes once b3270 has reported the connection closed (or the process has ended), not
    /// merely once it has accepted the Disconnect action, so callers can rely on the state afterwards.</summary>
    public async Task DisconnectAsync()
    {
        if (_process is null) return;
        if (ConnectionState == ConnectionState.Disconnected) return;
        await RunRawAsync([new B3270Action("Disconnect")]);
        await WaitForDisconnectedAsync();
    }

    /// <summary>Waits until b3270 reports the connection closed, or until <see cref="DisconnectTimeout"/> passes.
    /// The source belongs to the connection state, so a wait that starts while the state is already Disconnected
    /// returns at once, and overlapping callers await the same source whatever order they arrive in or give up in
    /// (spec 8).</summary>
    private async Task WaitForDisconnectedAsync()
    {
        try
        {
            await Volatile.Read(ref _disconnected).Task.WaitAsync(DisconnectTimeout);
        }
        catch (TimeoutException)
        {
            // b3270 accepted the action but never reported the state; the ConnectionChanged event still
            // fires if it does later. Hanging the caller would be worse.
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
        RequireProcess();
        var context = new TransferContext(progress);
        if (Interlocked.CompareExchange(ref _transfer, context, null) is not null)
            throw new InvalidOperationException("A file transfer is already in progress.");
        try
        {
            // b3270 does not answer the Transfer run until the transfer ends, so this run-result is the outcome,
            // a cancel included: the engine reports a cancelled transfer as a failure carrying its own text, and a
            // host failure that lands in the same moment keeps the host's text instead of being rewritten.
            var run = RunRawAsync([TransferMapper.ToAction(request)]);
            using var registration = cancellationToken.Register(() => context.Cancel = Task.Run(TryCancelTransferAsync));
            var result = await run;
            return new FileTransferResult(result.Success, string.Join("\n", result.Text));
        }
        finally
        {
            // The registration is disposed by now, so Cancel is final. A cancel that went out stays on this
            // slot's watch until b3270 has answered it, so it can never land on a transfer started after this one.
            if (Volatile.Read(ref context.Cancel) is { } cancel) await cancel;
            Interlocked.CompareExchange(ref _transfer, null, context);
        }
    }

    /// <summary>Sends Transfer(Cancel) off the cancelling thread and swallows the answer: b3270 says "No transfer
    /// pending." if the transfer already ended, and a dead engine faults the pending Transfer run on its own.</summary>
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
        /// <summary>The Transfer(Cancel) run once the token fired; awaited before the slot is released.</summary>
        public Task? Cancel;
    }
}
