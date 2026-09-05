using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeEmulatorSession : IEmulatorSession
{
    public SessionProfile Profile { get; set; } = new() { Name = "Fake", Host = "fake.host", Port = 3270 };
    public ScreenSnapshot CurrentScreen { get; set; } = ScreenSnapshot.Empty(24, 80);
    public ConnectionState ConnectionState { get; set; }
    public TlsInfo? Tls { get; set; }
    public KeyboardStatus KeyboardStatus { get; set; } = KeyboardStatus.Initial;
    public EngineInfo Engine { get; set; } = new("fake", null, "/fake/engine", EngineSource.Bundled);
    public string? WireLogPath { get; set; }
    /// <summary>When set, StartWireLog throws it.</summary>
    public Exception? WireLogException { get; set; }
    public List<string> Calls { get; } = [];
    public Exception? ConnectException { get; set; }
    public CancellationToken ConnectToken { get; private set; }
    /// <summary>When set, ConnectAsync waits for it, faulting with the token's cancellation if that comes first,
    /// so a test can drive a pending attempt through the view model's timeout or Disconnect.</summary>
    public TaskCompletionSource? ConnectCompletion { get; set; }
    public Exception? ActionException { get; set; }
    public FileTransferRequest? LastTransferRequest { get; private set; }
    public IProgress<long>? TransferProgress { get; private set; }
    public CancellationToken TransferToken { get; private set; }
    public FileTransferResult TransferResult { get; set; } = new(true, "Transfer complete, 12 bytes transferred");
    /// <summary>When set, TransferAsync throws it (after TransferCompletion, if that is set too).</summary>
    public Exception? TransferException { get; set; }
    /// <summary>When set, TransferAsync waits for it before answering, so a test can push progress through
    /// TransferProgress and cancel through TransferToken while the dialog is in its Running phase.</summary>
    public TaskCompletionSource? TransferCompletion { get; set; }

    public event EventHandler<ScreenSnapshot>? ScreenUpdated;
    public event EventHandler<KeyboardStatus>? StatusChanged;
    public event EventHandler<ConnectionState>? ConnectionChanged;
    public event EventHandler<BackendFault>? Faulted;
    public event EventHandler<string>? HostMessage;

    public async Task ConnectAsync(ConnectOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls.Add(options?.VerifyCertificate == false ? "connect:noverify" : "connect");
        ConnectToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        if (ConnectCompletion is { } completion)
        {
            using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            await completion.Task;
        }
        if (ConnectException is not null) throw ConnectException;
    }

    public void StartWireLog(string path)
    {
        Calls.Add("wirelog:start:" + path);
        if (WireLogException is not null) throw WireLogException;
        if (WireLogPath is not null) throw new InvalidOperationException("A wire log is already active.");
        WireLogPath = path;
    }

    public void StopWireLog()
    {
        Calls.Add("wirelog:stop");
        WireLogPath = null;
    }

    public Task DisconnectAsync() => Record("disconnect");
    public Task SendKeyAsync(TerminalKey key) => Record("key:" + key);
    public Task TypeTextAsync(string text) => Record("type:" + text);
    public Task PasteTextAsync(string text) => Record("paste:" + text);
    public Task MoveCursorAsync(int row, int column) => Record($"move:{row},{column}");

    public async Task<FileTransferResult> TransferAsync(FileTransferRequest request, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        Calls.Add($"transfer:{request.Direction}:{request.HostFile}");
        LastTransferRequest = request;
        TransferProgress = progress;
        TransferToken = cancellationToken;
        if (TransferCompletion is { } completion) await completion.Task;
        if (TransferException is not null) throw TransferException;
        return TransferResult;
    }

    public ValueTask DisposeAsync()
    {
        Calls.Add("dispose");
        return ValueTask.CompletedTask;
    }

    public void RaiseScreen(ScreenSnapshot screen) { CurrentScreen = screen; ScreenUpdated?.Invoke(this, screen); }
    public void RaiseStatus(KeyboardStatus status) { KeyboardStatus = status; StatusChanged?.Invoke(this, status); }
    public void RaiseConnection(ConnectionState state, TlsInfo? tls = null) { ConnectionState = state; Tls = tls; ConnectionChanged?.Invoke(this, state); }
    public void RaiseFault(BackendFault fault) => Faulted?.Invoke(this, fault);
    public void RaiseHostMessage(string message) => HostMessage?.Invoke(this, message);

    private Task Record(string call)
    {
        Calls.Add(call);
        return ActionException is null ? Task.CompletedTask : Task.FromException(ActionException);
    }
}
