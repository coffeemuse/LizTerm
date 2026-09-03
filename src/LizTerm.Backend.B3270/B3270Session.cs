using System.Collections.Concurrent;
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

    // ScreenUpdated and StatusChanged are raised once HandleStateIndication is filled in (Task 10).
#pragma warning disable CS0067
    public event EventHandler<ScreenSnapshot>? ScreenUpdated;
    public event EventHandler<KeyboardStatus>? StatusChanged;
#pragma warning restore CS0067
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
        _hello = new TaskCompletionSource<HelloIndication>(TaskCreationOptions.RunContinuationsAsynchronously);
        _process = process;
        process.Start(BuildArguments(Profile));
        _readerThread = new Thread(ReadLoop) { IsBackground = true, Name = "b3270-reader" };
        _readerThread.Start();

        HelloIndication hello;
        try
        {
            hello = await _hello.Task.WaitAsync(StartupTimeout, cancellationToken);
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
        catch (Exception ex) when (_shuttingDown || ex is ObjectDisposedException or IOException)
        {
            // Stream closed during shutdown.
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
            if (!_shuttingDown) Faulted?.Invoke(this, fault);
        }
        catch (Exception)
        {
            // Swallow: an exception here must not escape the reader thread.
        }
    }

    private void TearDown()
    {
        _shuttingDown = true;
        _process?.Kill();
        _process?.Dispose();
        _process = null;
    }

    public async ValueTask DisposeAsync()
    {
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
        _wireLog?.Dispose();
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

    /// <summary>Screen, OIA, connection, TLS, popup handling. Filled in by the next task.</summary>
    private void HandleStateIndication(Indication indication)
    {
    }

    private void SetConnectionState(ConnectionState state)
    {
        if (ConnectionState == state) return;
        ConnectionState = state;
        ConnectionChanged?.Invoke(this, state);
    }

    // ---- IEmulatorSession actions (completed in the next task) ----

    public Task ConnectAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task DisconnectAsync() => throw new NotImplementedException();
    public Task SendKeyAsync(TerminalKey key) => throw new NotImplementedException();
    public Task TypeTextAsync(string text) => throw new NotImplementedException();
    public Task PasteTextAsync(string text) => throw new NotImplementedException();
    public Task MoveCursorAsync(int row, int column) => throw new NotImplementedException();
}
