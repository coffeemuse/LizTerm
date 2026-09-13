// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.Concurrent;
using System.Globalization;
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
    /// <summary>Volatile because RunAsync re-reads it on a caller thread to catch the reader thread clearing it
    /// (see OnProcessEnded for the order that makes the re-read sufficient).</summary>
    private volatile IB3270Process? _process;
    private Thread? _readerThread;
    private TaskCompletionSource<HelloIndication>? _hello;
    /// <summary>The Set() toggle names the engine's own tls-hello reported (plan 3d task 8), or null when
    /// tls-hello has never been observed on this process. Set once, synchronously, before <see cref="_hello"/>
    /// completes (see the InitializeIndication case in <see cref="Handle"/>), so anything that awaited
    /// <see cref="StartProcessAsync"/> sees it, and cleared alongside <see cref="_process"/> and <see cref="_hello"/>
    /// wherever they are (TearDown, and OnProcessEnded's non-shutdown path) so a dead process's option set can
    /// never gate a toggle for the fresh one a later ConnectAsync spawns. Null is read as "supports everything
    /// this session ever gates", not "supports nothing": every b3270 build before this indication existed behaved
    /// that way, and treating unknown as unsupported would silently take caFile-backed trust away from
    /// macOS/Linux engines that always had it, purely because a test double or an old override never sent the
    /// indication -- the same reading applies to the gap between a process ending and its replacement's own
    /// tls-hello arriving.</summary>
    private IReadOnlyList<string>? _tlsOptions;
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

    /// <summary>Whether this session's engine can honour a certificate pin at all (spec: plan 3d task 8). A
    /// Schannel engine's tls-hello lists no "caFile", so a pin can never be handed to it; before the process has
    /// even started, or before tls-hello has arrived on it, this reads true (see <see cref="_tlsOptions"/>).
    /// Expressed here in Core's own vocabulary — "can this session pin", not "does the engine list caFile" —
    /// because IEmulatorSession must not know b3270's toggle names exist.</summary>
    public bool CanPinCertificates => SupportsTlsOption("caFile");

    /// <summary>All three toggles this session ever sends in one Set (spec item 3), used when tls-hello has not
    /// arrived: see <see cref="_tlsOptions"/> for why "not yet known" defaults to "assume supported".</summary>
    private static readonly string[] AllGateableTlsOptions = ["verifyHostCert", "caFile", "acceptHostname"];

    /// <summary>What <see cref="TlsSettings"/> gates each toggle on: the engine's own reported list, or every
    /// toggle this session knows about when tls-hello has not arrived yet (see <see cref="_tlsOptions"/>).</summary>
    private IReadOnlyList<string> EffectiveTlsOptions => _tlsOptions ?? AllGateableTlsOptions;

    private bool SupportsTlsOption(string name) => EffectiveTlsOptions.Contains(name, StringComparer.Ordinal);

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
    public event EventHandler? BellRang;

    /// <summary>The engine's command line. Oversize and the keep-alive ride here rather than as runtime Set
    /// actions: it needs no new protocol handling, and it sidesteps the unverified question of whether a runtime
    /// oversize change takes effect on the next connect, on the next process, or not at all. `reconnect` is the
    /// exception and must be a runtime Set, because it is armed only after a connect has succeeded.
    /// Both are omitted at their defaults, which are b3270's own.</summary>
    /// <exception cref="ConnectionFailedException">The profile's oversize is one b3270 would refuse.</exception>
    public static IReadOnlyList<string> BuildArguments(SessionProfile profile)
    {
        var arguments = new List<string>
        {
            "-json", "-utf8", "-model", HostStringBuilder.ModelArgument(profile), "-codepage", profile.CodePage,
        };
        if (!string.IsNullOrWhiteSpace(profile.Oversize))
        {
            // Checked here, not only in the profile editor. A profile file is user-editable and nothing else on
            // the way in looks at this field — ProfileStore.Read sanitises a broken pin and stops there — so a
            // hand-edited "200x200", or a model raised to 5 under an oversize that only ever cleared model 2's
            // floor, would reach argv unread. That is the exact outcome OversizeGeometry exists to prevent
            // (spec 4.2): b3270 answers a bad -oversize with a popup, which arrives as an unexplained HostMessage
            // with nothing naming the field the user typed. Throwing names it, and lands in the same error banner
            // DecideCaFile's own refusal does, before a single action is written.
            var model = TerminalModel.Find(profile.Model) ?? new TerminalModel(profile.Model, 0, 0);
            if (!OversizeGeometry.TryParse(profile.Oversize, model, out var oversize, out var error))
                throw new ConnectionFailedException([error!]);
            // Null for b3270's own "0x0" spelling of none, which is valid and means: send nothing.
            if (oversize is not null)
            {
                arguments.Add("-oversize");
                arguments.Add(oversize.ToString());
            }
        }
        if (profile.KeepAliveSeconds > 0)
        {
            // "-set name=value" is the only form the engine takes: "-nopSeconds 60" is not an option at all.
            arguments.Add("-set");
            arguments.Add($"nopSeconds={profile.KeepAliveSeconds.ToString(CultureInfo.InvariantCulture)}");
        }
        return arguments;
    }

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
            _hello?.TrySetException(new BackendUnavailableException(fault.Message + " stderr: " + string.Join(" | ", process.StderrTail)));

            SetConnectionState(ConnectionState.Disconnected);
            if (!_shuttingDown)
            {
                // The fault is the session's whole state before anyone hears of it. The slot is cleared and _fault
                // set here, ahead of both the drain below and the Faulted event after it, so that a caller who
                // reacts to the event by calling straight back in meets RequireProcess's
                // BackendUnavailableException and not a process about to vanish. The old order (drain, raise,
                // then clear) let such a call pass RequireProcess, register a run after the drain and wait on it
                // forever; CI hung for the blame timeout on exactly that on 2026-09-13.
                //
                // Clearing the slot also lets a later ConnectAsync spawn a fresh process through the factory
                // instead of being stuck thinking the dead one is still usable.
                _fault = fault;
                process.Dispose();
                _process = null;
                _hello = null;
                // The dead process's tls-hello answer dies with it (finding 4, plan 3d task 8 review): the next
                // process gets its own hello/tls-hello round trip, and until that arrives _tlsOptions must read
                // as "not yet known" (null), not as whatever this process happened to report.
                _tlsOptions = null;
            }

            // Drained after the slot is cleared, never before: a run that passed RequireProcess earlier is either
            // already registered here, or registers later and fails RunAsync's own re-read of the slot. Either
            // way it ends with the fault. A shutdown keeps the slot (DisposeAsync owns it) but drains all the same.
            foreach (var tag in _pending.Keys.ToArray())
                if (_pending.TryRemove(tag, out var tcs))
                    tcs.TrySetException(new BackendUnavailableException(fault.Message));

            if (!_shuttingDown) Faulted?.Invoke(this, fault);
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
        _tlsOptions = null;
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
            // the lines a report about a hang on close turns on. Both CA files go the same way: each is kept
            // across a session's connects, so this is where their life ends.
            _rootsFile.Delete();
            _pinFile.Delete();
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
        var process = RequireProcess();
        var tag = Interlocked.Increment(ref _tagCounter).ToString();
        var tcs = new TaskCompletionSource<RunResultIndication>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[tag] = tcs;
        // The reader thread clears the slot before it drains _pending (OnProcessEnded), so re-reading the slot
        // after registering closes the one window a dying engine leaves: if the slot still holds this process,
        // the drain has not run yet and will fault this tag; if it does not, nothing ever will, so the tag goes
        // and the caller gets the fault now rather than a wait with no end.
        if (!ReferenceEquals(_process, process))
        {
            _pending.TryRemove(tag, out _);
            throw NoProcess();
        }
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
    private IB3270Process RequireProcess() => _process ?? throw NoProcess();

    private Exception NoProcess() =>
        _fault is { } fault
            ? new BackendUnavailableException(fault.Message)
            : new InvalidOperationException("The session has not been started.");

    // ---- indications ----

    private void Handle(Indication indication)
    {
        switch (indication)
        {
            case InitializeIndication init:
                // b3270 builds this whole block with one uij_open_array/uij_close_array pair (Common/b3270/b3270.c)
                // and prints it as a single line, so every item here — tls-hello included — is already sitting in
                // init.Items by the time this case runs; there is no second line to race against. The race that
                // *does* exist is on our side: _hello.TrySetResult (RunContinuationsAsynchronously) only queues
                // StartProcessAsync's continuation, it does not block this reader thread, so completing it from the
                // nested HelloIndication case below — while this foreach still has later items (tls-hello among
                // them) left to dispatch — would let that continuation run on a thread pool thread concurrently
                // with the rest of this loop. ConnectAsync (awaited after StartProcessAsync returns) would then be
                // reading _tlsOptions on a race it could lose, wrongly concluding the engine supports nothing and
                // silently dropping caFile even from an engine that does support it — the same silent downgrade
                // this task exists to close, by a second door. Deferring the TrySetResult to the end of this loop
                // closes it deliberately: every item this initialize block carries, including tls-hello, has already
                // updated session state (synchronously, on this thread) before anything waiting on hello can resume.
                // The foreach is wrapped in try/finally for the same reason, one door over (review finding 2 on
                // this task): a later item can itself throw -- a malformed indication, or an external
                // ScreenUpdated/StatusChanged subscriber throwing, which the App's dispatch delegate can do during
                // shutdown -- and ReadLoop's inner try/catch (see below) only swallows that into a HostMessage, it
                // does not resume this loop. Without the finally, that throw would skip TrySetResult entirely and
                // leave _hello incomplete, so a healthy engine that answered hello just fine gets blamed for a
                // spurious "did not answer within N seconds" report -- a fault in our own handler misreported as
                // the engine's.
                HelloIndication? hello = null;
                try
                {
                    foreach (var item in init.Items)
                    {
                        if (item is HelloIndication h) hello = h;
                        else Handle(item);
                    }
                }
                finally
                {
                    if (hello is not null) _hello?.TrySetResult(hello);
                }
                break;
            case HelloIndication bareHello:
                // Reached only if a hello ever arrives outside an initialize block — not how the real engine
                // behaves (b3270 sends it nowhere else), but nothing else in a lone line can race against it.
                _hello?.TrySetResult(bareHello);
                break;
            case TlsHelloIndication tlsHello:
                // Supported:false carries no "options" key at all, and the parser already degrades a missing or
                // malformed one to an empty list, so this assignment needs no extra branch for that case.
                _tlsOptions = tlsHello.Options;
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
            case BellIndication:
                BellRang?.Invoke(this, EventArgs.Empty);
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
        // synchronous write below — would run on that thread. DecideCaFile is the whole decide-and-write step, so
        // the CA file is fully written (or the source's short-circuit fully resolved) before the thread pool hop
        // back to the caller. The token goes with it: an attempt already cancelled skips work whose result is
        // known to be discarded, the way every other await on this path does.
        var (caFile, anyName) = await Task.Run(() => DecideCaFile(pin, verify), cancellationToken);
        LastCaFile = caFile;
        // Bounded by the caller's token: b3270 answers Set at once, but a wedged engine must not hold the attempt
        // open before the Connect has even gone out. Every toggle this engine's tls-hello listed is sent every
        // time so an attempt never inherits the previous one's trust settings (spec 2); TlsSettings returns null
        // when tls-hello listed none of the three, in which case there is nothing to run at all.
        if (TlsSettings(verify, caFile, anyName, EffectiveTlsOptions) is { } set)
            await RunAsync([set], throwOnFailure: true, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await ConnectCoreAsync(cancellationToken);
        // Armed here and nowhere earlier. b3270's other toggle, `retry`, would keep retrying a connect that
        // failed, and that is what collides with everything ConnectAsync is built on: a failed Connect run
        // meaning the attempt is over is what raises ConnectionFailedException, what feeds the certificate
        // prompt, and what SessionViewModel's 30s timeout measures. `reconnect` armed after success touches
        // none of it (spec 6.1).
        //
        // Not throwOnFailure, no caller token, and every failure swallowed: the connect has already
        // succeeded, so nothing past this point may turn that success into a thrown exception and an error
        // banner. throwOnFailure: false only covers a refused run-result; RunAsync writes the line before it
        // ever looks at a token, so honouring the caller's cancellationToken here would arm a live engine and
        // then still throw OperationCanceledException out of ConnectAsync if it fired mid-round-trip -- exactly
        // the "leaves the session disconnected and reusable" contract on IEmulatorSession.ConnectAsync that a
        // cancel is supposed to keep. So this run takes no token, is bounded by DisconnectTimeout instead so a
        // wedged engine cannot hang an otherwise-successful connect, and the try/catch below swallows anything
        // that still gets past that -- a dead engine (BackendUnavailableException), a closed stdin
        // (IOException), or the bounded wait's own TimeoutException. Losing auto-reconnect is a far smaller
        // loss than reporting a working connect as a failure, and a genuinely dead engine still corrects
        // itself regardless: OnProcessEnded raises the fault and drops the connection state on its own.
        //
        // Swallowed is not the same as unreported, though. An engine whose `reconnect` toggle is absent or
        // refused leaves auto-reconnect off while the profile's checkbox still says it is on, and the user only
        // finds out when a dropped session never comes back -- the same silent capability downgrade DecideCaFile
        // refuses to make for a pin. So the outcome is read and said out loud, as a HostMessage: the session is
        // up and stays up, and the one thing that did not happen is named. (Raised from the connect's own thread
        // rather than the reader's, as StartProcessAsync already does for the wire-log warning.)
        if (Profile.AutoReconnect)
        {
            try
            {
                var armed = await RunAsync([new B3270Action("Set", "reconnect", "true")], throwOnFailure: false, timeout: DisconnectTimeout);
                if (!armed.Success) ReportReconnectUnavailable(string.Join(" ", armed.Text));
            }
            catch (Exception ex)
            {
                ReportReconnectUnavailable(ex.Message);
            }
        }
    }

    /// <summary>Auto-reconnect is on in the profile and the engine would not have it. Says so rather than leaving
    /// the user to discover it when a dropped session never returns; the connect itself has already succeeded and
    /// is not disturbed.</summary>
    private void ReportReconnectUnavailable(string reason) =>
        HostMessage?.Invoke(this, "Automatic reconnect could not be turned on for this session"
            + (string.IsNullOrWhiteSpace(reason) ? "." : ": " + reason));

    /// <summary>The trust decision for one connect attempt, and the file that carries it: spec 3, a pin is the whole
    /// trust store; without one, a verifying connect gets the machine's own anchors, because a statically linked
    /// engine has none it can use (spec 1); verification off gets no CA file at all. Evaluated off the caller's
    /// context (see <see cref="ConnectAsync"/>)
    /// since it is the only part of a connect that does real work: reading the trust source and writing its PEM to
    /// disk. Either file, once written, lives until <see cref="DisposeAsync"/>; see <see cref="SessionCaFile"/>
    /// for why neither may be deleted after the Connect run that used it.</summary>
    private (string? CaFile, bool AnyName) DecideCaFile(CertificatePin? pin, bool verify)
    {
        // A pin is the whole trust store, so it never reaches the trust-anchor source. Pem is declared
        // non-nullable but arrives from a user-editable profile file, so a pin that lost it must still fail the
        // connect loudly — b3270 answers the resulting empty caFile with "CA database load … failed" — rather
        // than falling through to the wider trust of the anchors below.
        if (pin is not null)
        {
            // Spec item 4, the most important rule in this task: an engine that cannot accept caFile can never
            // honour a pin, and proceeding anyway would not fail — it would silently verify against whatever trust
            // the engine falls back to (a Schannel engine's own Windows certificate store) while the profile (or
            // the one-shot Connect Anyway pin) still says a pin is in force. That is a silent security downgrade,
            // and strictly worse than refusing to connect: the user would believe a specific certificate was
            // required when in fact anything the platform trusts would do. Throwing here, before a single Set or
            // Connect action is built, is what "loudly" means — DecideCaFile runs before either is ever sent.
            if (!CanPinCertificates)
                throw new ConnectionFailedException([
                    "This engine's TLS provider cannot verify a pinned certificate (it has no caFile option). " +
                    "Connecting would silently trust the platform certificate store instead of the pin.",
                ]);
            var pinPem = pin.Pem ?? "";
            // A pin that is one self-signed certificate names the host by itself, so the name check adds nothing.
            // A pin that also carries CA certificates makes each of them a trust anchor (OpenSSL trusts every
            // member of caFile), and only the engine's normal name check then keeps a certificate that CA issued
            // for another host from verifying here.
            // Kept for the session, exactly as the roots file is: the engine rebuilds its TLS context — and so
            // reloads caFile — for every connection it makes, including the ones it starts itself once
            // `reconnect` is armed (spec 6.1), so a file deleted after this Connect run answered would leave a
            // pinned profile unable to reconnect, forever. See SessionCaFile.
            return (_pinFile.PathFor(pinPem), CertificateReader.CountCertificates(pinPem) == 1);
        }
        if (!CanPinCertificates)
        {
            // Spec item 5: no pin is in force, and this engine has nowhere to put anchors even if we read them.
            // A Schannel engine verifies against the Windows certificate store natively, once verifyHostCert is on
            // — exactly the outcome plan 3b engineered for macOS and Linux by handing them the *same* store through
            // caFile, so this is not a downgrade to "fix" by wiring the anchors back in; it is the platform doing
            // on its own what caFile exists to do on the platforms that need it spelled out. Reading TrustAnchors
            // (a measured 210 ms) and writing its PEM to a file the engine has no toggle to ever be pointed at
            // would just be work spent restoring an anchor this platform never lost.
            return (null, false);
        }
        // NOT gated on Profile.UseTls, however tempting: b3270 implements the TELNET START-TLS option, so a plain
        // profile can still upgrade to TLS mid-session, and an attempt that reached that point with an empty
        // caFile would verify against the engine's own compiled-in directory — the nonexistent Homebrew path this
        // milestone exists to stop relying on. The cost that made the gate look attractive is gone anyway: the
        // anchors are read once per process and the file is written once per session.
        var anchors = verify ? TrustAnchors.ExportPem() : null;
        // A source with nothing to offer, or only whitespace, leaves caFile empty: an empty *file* fails the
        // connect outright, so "nothing usable" has to collapse to null before it reaches the file.
        return (string.IsNullOrWhiteSpace(anchors) ? null : _rootsFile.PathFor(anchors), false);
    }

    private readonly SessionCaFile _rootsFile = new("roots");
    private readonly SessionCaFile _pinFile = new("pin");

    /// <summary>One CA file, written on first use and kept for the session: reused by every later connect while
    /// its PEM is unchanged, rewritten when it is not, and removed by <see cref="DisposeAsync"/>.
    /// <para>Session-lived rather than per-attempt because x3270 rebuilds its TLS context for every connection —
    /// <c>finish_connect</c> (4.5ga6 <c>Common/telnet.c:568-579</c>) calls <c>sio_init</c>, which hands
    /// <c>caFile</c> to <c>SSL_CTX_load_verify_locations</c>, and a load failure is <c>SI_FAILURE</c> →
    /// <c>NC_FAILED</c>. The engine makes those connections on its own once <c>reconnect</c> is armed, so a file
    /// deleted after the Connect run that used it would fail every reconnect with "CA database load … failed",
    /// and, since <c>host_retry_mode</c> keeps the reconnect armed across that failure, would go on failing every
    /// few seconds indefinitely. Both files are public certificates, so the secrecy argument that once justified
    /// deleting the pin file promptly never outweighed that.</para>
    /// <para>Keyed on the PEM rather than written once because <see cref="ConnectOptions.Pin"/> can carry a
    /// different pin per attempt within one session: a changed pin has to get its own file rather than silently
    /// reuse the last one.</para></summary>
    private sealed class SessionCaFile(string kind)
    {
        private readonly object _lock = new();
        private string? _path;
        private string? _pem;

        public string PathFor(string pem)
        {
            string path;
            string? superseded;
            lock (_lock)
            {
                if (_path is not null && _pem == pem && File.Exists(_path)) return _path;
                superseded = _path;
                _path = path = WriteCaFile(pem, kind);
                _pem = pem;
            }
            // The PEM changed, so the previous file will never be named again: delete it here rather than leaving
            // it in the temp directory until the process ends.
            if (superseded is not null) TryDeleteCaFile(superseded);
            return path;
        }

        public void Delete()
        {
            string? path;
            lock (_lock)
            {
                path = _path;
                _path = null;
                _pem = null;
            }
            if (path is not null) TryDeleteCaFile(path);
        }
    }

    /// <summary>The Set action carrying one attempt's trust settings (spec 4.1), or null when
    /// <paramref name="supportedOptions"/> names none of the three: b3270 reads a bare Set() (zero pairs) as
    /// "show all toggles" rather than an error, but there is nothing useful in sending it, so the caller skips the
    /// run entirely rather than spending a round trip on a no-op. Each toggle is gated independently on whether its
    /// name is in <paramref name="supportedOptions"/> — exactly the list the engine itself reported in tls-hello.
    /// This is deliberately not <c>OperatingSystem.IsWindows()</c> and not a check on the provider string: the
    /// option list is the engine's own statement of what it accepts, already on the wire, and it stays correct for
    /// a provider nobody has special-cased yet (a real Schannel engine, for what it is worth, still lists
    /// verifyHostCert and acceptHostname — b3270 treats those as TLS-required regardless of provider — so on the
    /// real Windows build only caFile ever drops out; a provider that also lacks the other two is exercised here
    /// only as the stricter, hypothetical case). <paramref name="acceptAnyName"/> turns the engine's host-name
    /// check off, which is right only for a pin that is a single self-signed certificate; an empty acceptHostname
    /// is the engine's normal check against the connect host. An empty caFile leaves the engine on its own default
    /// trust.</summary>
    internal static B3270Action? TlsSettings(bool verify, string? caFile, bool acceptAnyName, IReadOnlyList<string> supportedOptions)
    {
        var pairs = new List<string>();
        if (supportedOptions.Contains("verifyHostCert", StringComparer.Ordinal))
        {
            pairs.Add("verifyHostCert");
            pairs.Add(verify ? "true" : "false");
        }
        if (supportedOptions.Contains("caFile", StringComparer.Ordinal))
        {
            pairs.Add("caFile");
            pairs.Add(caFile ?? "");
        }
        if (supportedOptions.Contains("acceptHostname", StringComparer.Ordinal))
        {
            pairs.Add("acceptHostname");
            pairs.Add(caFile is not null && acceptAnyName ? "any" : "");
        }
        return pairs.Count == 0 ? null : new B3270Action("Set", [.. pairs]);
    }

    /// <summary>The path of the CA file the last connect pointed the engine at — a pin or the trust anchors — or
    /// null when that connect sent none. Both kinds live until <see cref="DisposeAsync"/>. Test seam.</summary>
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
                // The budget for the pending Connect run is started inside TryDisconnectQuietlyAsync, once the
                // Disconnect is actually about to go out. Started here instead, an armed profile's disarm — itself
                // bounded by DisconnectTimeout — could spend the entire window against a wedged engine before the
                // Disconnect was even written, and the run would be abandoned for a reply nothing had yet asked for.
                Volatile.Write(ref disconnect, Task.Run(() => TryDisconnectQuietlyAsync(runCts)))))
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

    /// <param name="pendingConnect">The pending Connect run's own source, when this is a cancel. It is the
    /// Disconnect below that makes b3270 fail that run, so the run is allowed <see cref="DisconnectTimeout"/> from
    /// the moment the Disconnect can go out — after the disarm, not before it — and no longer: a wedged engine
    /// must not hold the attempt open past its cancellation. The caller awaits this task before the source leaves
    /// scope, so the deadline can never be set on a disposed one.</param>
    private async Task TryDisconnectQuietlyAsync(CancellationTokenSource? pendingConnect = null)
    {
        try
        {
            // The cancel path disconnects too, so it needs the same disarm: a user pressing Connect again while
            // a reconnect is churning must not leave the old intent behind. In a finally, so a disarm that fails
            // in some way its own catch does not cover still cannot leave the pending Connect run with no deadline
            // at all — which would hang the cancel rather than merely shorten it.
            try { await DisarmReconnectAsync(); }
            finally { pendingConnect?.CancelAfter(DisconnectTimeout); }
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
        // Before the early-out, deliberately. Measured against 4.5ga6: with reconnect armed, b3270 reconnects
        // from the Disconnect action itself — the state goes straight to `reconnecting` and the session is back
        // two seconds later — so a Disconnect sent without this is silently undone. The early-out turns out to
        // be unreachable during a reconnect anyway, because the engine never reports not-connected while armed;
        // the disarm stays ahead of it because that is an engine behaviour we have measured once and cannot
        // enforce, and one extra action on this path costs nothing (spec 6.2).
        await DisarmReconnectAsync();
        if (ConnectionState == ConnectionState.Disconnected) return;
        await RunRawAsync([new B3270Action("Disconnect")]);
        await WaitForDisconnectedAsync();
    }

    /// <summary>Turns b3270's own reconnect off, which is the only thing that stops one: the Disconnect action
    /// does not clear the intent. It is also what makes the engine report `not-connected` during a reconnect —
    /// about 50 ms later, measured — which is the state <see cref="WaitForDisconnectedAsync"/> is waiting for.
    /// Quiet, and bounded: the caller is on its way to disconnecting, and neither a refused Set nor a wedged
    /// engine may be what stops it.</summary>
    private async Task DisarmReconnectAsync()
    {
        if (!Profile.AutoReconnect || _process is null) return;
        try
        {
            await RunRawAsync([new B3270Action("Set", "reconnect", "false")], DisconnectTimeout);
        }
        catch (Exception)
        {
            // The process may be gone, or the engine may accept the Set and never answer it. The Disconnect
            // that follows deals with both.
        }
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
