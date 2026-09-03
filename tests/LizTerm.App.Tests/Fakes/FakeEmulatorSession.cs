using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeEmulatorSession : IEmulatorSession
{
    public SessionProfile Profile { get; init; } = new() { Name = "Fake", Host = "fake.host", Port = 3270 };
    public ScreenSnapshot CurrentScreen { get; set; } = ScreenSnapshot.Empty(24, 80);
    public ConnectionState ConnectionState { get; set; }
    public TlsInfo? Tls { get; set; }
    public KeyboardStatus KeyboardStatus { get; set; } = KeyboardStatus.Initial;
    public List<string> Calls { get; } = [];
    public Exception? ConnectException { get; set; }
    public Exception? ActionException { get; set; }

    public event EventHandler<ScreenSnapshot>? ScreenUpdated;
    public event EventHandler<KeyboardStatus>? StatusChanged;
    public event EventHandler<ConnectionState>? ConnectionChanged;
    public event EventHandler<BackendFault>? Faulted;
    public event EventHandler<string>? HostMessage;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("connect");
        return ConnectException is null ? Task.CompletedTask : Task.FromException(ConnectException);
    }

    public Task DisconnectAsync() => Record("disconnect");
    public Task SendKeyAsync(TerminalKey key) => Record("key:" + key);
    public Task TypeTextAsync(string text) => Record("type:" + text);
    public Task PasteTextAsync(string text) => Record("paste:" + text);
    public Task MoveCursorAsync(int row, int column) => Record($"move:{row},{column}");

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
