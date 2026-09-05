using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.Status;

/// <summary>Plain-language status text. Glyphs are ones the IBM 3270 font encodes: its padlock (U+E0A2) and ordinary check/cross marks.</summary>
public static class StatusFormatter
{
    public const string PadlockGlyph = "\uE0A2";
    public const string ReadyGlyph = "✓";
    public const string BlockedGlyph = "✕";

    public static string Connection(ConnectionState state, string host) => state switch
    {
        ConnectionState.Disconnected => "Not connected",
        ConnectionState.Reconnecting => $"Reconnecting to {host}",
        ConnectionState.Resolving => $"Looking up {host}",
        ConnectionState.TcpPending => $"Connecting to {host}",
        ConnectionState.TlsPending => $"Securing connection to {host}",
        ConnectionState.TlsPasswordPending => "Waiting for TLS key password",
        ConnectionState.ProxyPending => $"Connecting to {host} through proxy",
        ConnectionState.TelnetPending => $"Negotiating with {host}",
        ConnectionState.ConnectedNvt or ConnectionState.ConnectedNvtCharMode or ConnectionState.ConnectedENvt => $"Connected to {host} (NVT)",
        ConnectionState.Connected3270 => $"Connected to {host} (TN3270)",
        ConnectionState.ConnectedUnbound => $"Connected to {host} (TN3270E, unbound)",
        ConnectionState.ConnectedSscp => $"Connected to {host} (SSCP-LU)",
        ConnectionState.ConnectedTn3270E => $"Connected to {host} (TN3270E)",
        _ => state.ToString(),
    };

    public static string Tls(TlsInfo? tls) => tls is { Secure: true }
        ? $"{PadlockGlyph} TLS, certificate {(tls.Verified == true ? "verified" : "not verified")}"
        : "";

    public static string Keyboard(KeyboardStatus status) => status.Lock switch
    {
        KeyboardLock.Unlocked => $"{ReadyGlyph} Ready",
        KeyboardLock.NotConnected => $"{BlockedGlyph} Not connected",
        KeyboardLock.WaitingForHost => $"{BlockedGlyph} Waiting for host",
        KeyboardLock.TerminalWait => $"{BlockedGlyph} Please wait",
        KeyboardLock.Deferred => $"{BlockedGlyph} Waiting for host",
        KeyboardLock.MinusFunction => $"{BlockedGlyph} Not available here, press Esc",
        KeyboardLock.ProtectedField => $"{BlockedGlyph} Protected field, press Esc",
        KeyboardLock.NumericOnly => $"{BlockedGlyph} Numbers only here, press Esc",
        KeyboardLock.Overflow => $"{BlockedGlyph} Field is full, press Esc",
        KeyboardLock.Dbcs => $"{BlockedGlyph} Invalid double-byte input, press Esc",
        KeyboardLock.Scrolled => $"{BlockedGlyph} Scrolled back",
        KeyboardLock.Disabled => $"{BlockedGlyph} Keyboard disabled",
        KeyboardLock.FieldWait => $"{BlockedGlyph} Waiting for field",
        KeyboardLock.FileTransfer => $"{BlockedGlyph} File transfer in progress",
        _ => $"{BlockedGlyph} {status.LockDetail ?? "Locked"}",
    };

    public static string Insert(bool on) => on ? "INS" : "";

    public static string Cursor(CursorPosition cursor) => $"{cursor.Row + 1:D2}/{cursor.Column + 1:D3}";

    public static string Model(SessionProfile profile, string? luName)
    {
        var model = $"Model {profile.Model}{(profile.Extended ? "-E" : "")}";
        return luName is null ? model : $"{model}  LU {luName}";
    }

    public static string Fault(BackendFault fault)
    {
        var tail = string.Join(" | ", fault.StderrTail.TakeLast(3));
        var code = fault.ExitCode?.ToString() ?? "unknown";
        var detail = tail.Length > 0 ? $" Last output: {tail}." : "";
        return $"{fault.Message} (exit code {code}).{detail} Turn on Help > Wire Log and reproduce to capture a log.";
    }

    /// <summary>The attempt never completed. A plain connect to a TLS listener reaches telnet-pending and then
    /// waits forever, so that exact signature earns the TLS hint.</summary>
    public static string ConnectTimeout(SessionProfile profile, TimeSpan timeout, bool reachedTelnet)
    {
        var text = $"Connection to {profile.Host}:{profile.Port} timed out after {timeout.TotalSeconds:0} seconds.";
        return reachedTelnet && !profile.UseTls ? text + " The host may require TLS. Enable it in the profile." : text;
    }

    public static string WireLog(bool active) => active ? "● wire log" : "";

    /// <param name="overrideOrigin">What pointed at an Override binary, named by the app (today the environment variable).</param>
    public static string Engine(EngineInfo engine, string overrideOrigin)
    {
        var name = engine.Version is null ? $"{engine.Name}, not started" : $"{engine.Name} {engine.Version}";
        return engine.Source == EngineSource.Bundled ? $"{name}, bundled" : $"{name}, from {overrideOrigin}";
    }
}
