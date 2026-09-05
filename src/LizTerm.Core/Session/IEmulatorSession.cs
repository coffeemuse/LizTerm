using LizTerm.Core.Screen;

namespace LizTerm.Core.Session;

/// <summary>
/// One terminal session bound to one profile. Events are raised on a backend thread,
/// in order; the caller marshals to its UI thread.
/// </summary>
public interface IEmulatorSession : IAsyncDisposable
{
    SessionProfile Profile { get; }
    ScreenSnapshot CurrentScreen { get; }
    ConnectionState ConnectionState { get; }
    TlsInfo? Tls { get; }
    KeyboardStatus KeyboardStatus { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);
    /// <summary>Completes once the session reports <see cref="ConnectionState.Disconnected"/>, or after a
    /// short backend-defined timeout if that report never comes. Does nothing when not connected.</summary>
    Task DisconnectAsync();
    Task SendKeyAsync(TerminalKey key);
    Task TypeTextAsync(string text);
    Task PasteTextAsync(string text);
    /// <summary>Zero-based row and column.</summary>
    Task MoveCursorAsync(int row, int column);

    /// <summary>Runs one IND$FILE transfer and completes when it ends. A transfer the engine or host refuses,
    /// aborts, or cancels is a result with <c>Succeeded</c> false and the message to show verbatim: a cancel
    /// requested through the token comes back as the engine's own text, and a host failure that lands in the
    /// same moment keeps the host's. Throws <see cref="InvalidOperationException"/> when the session was never
    /// started or a transfer is already running, <see cref="OperationCanceledException"/> only when the token was
    /// already cancelled on entry, and <see cref="BackendUnavailableException"/> when the engine has died or
    /// dies during the transfer. A success that beats a cancel is returned as success. Progress reports bytes so
    /// far on the backend thread and may never be called. On receive an existing local file is replaced; the
    /// caller obtains consent.</summary>
    Task<FileTransferResult> TransferAsync(FileTransferRequest request, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

    event EventHandler<ScreenSnapshot>? ScreenUpdated;
    event EventHandler<KeyboardStatus>? StatusChanged;
    event EventHandler<ConnectionState>? ConnectionChanged;
    event EventHandler<BackendFault>? Faulted;
    /// <summary>Informational or error text from the emulator or host, for display.</summary>
    event EventHandler<string>? HostMessage;
}
