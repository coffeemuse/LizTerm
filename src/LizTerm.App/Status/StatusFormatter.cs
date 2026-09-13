// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.Status;

/// <summary>Status bar text. The bar itself speaks x3270's Operator Information Area (see <see cref="OiaGlyphs"/>
/// and the x3270 wiki's "Operator Information Area" page); the plain-language sentences here go on tooltips, error
/// bars and the About window. The padlock is the font's Powerline one (U+E0A2).</summary>
public static class StatusFormatter
{
    public const string PadlockGlyph = "";
    public const string VerifiedMark = "✓";
    public const string UnverifiedMark = "!";

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

    /// <summary>x3270's mode field: the boxed 4 a 3270 always shows, A or B underlined for TN3270 or TN3270E once a
    /// connection is up, then the session: a solid box when bound, N for NVT, the boxed human for SSCP-LU, a boxed
    /// ? when there is none. x3270 takes the A/B from the host's own "under A" indication; deriving it from the
    /// connection state is the nearest thing the engine reports to the App.</summary>
    public static string Mode(ConnectionState state) => state switch
    {
        ConnectionState.ConnectedNvt or ConnectionState.ConnectedNvtCharMode => OiaGlyphs.Box4 + OiaGlyphs.UnderA + "N",
        ConnectionState.Connected3270 => OiaGlyphs.Box4 + OiaGlyphs.UnderA + OiaGlyphs.BoxSolid,
        ConnectionState.ConnectedUnbound => OiaGlyphs.Box4 + OiaGlyphs.UnderB + OiaGlyphs.BoxQuestion,
        ConnectionState.ConnectedENvt => OiaGlyphs.Box4 + OiaGlyphs.UnderB + "N",
        ConnectionState.ConnectedSscp => OiaGlyphs.Box4 + OiaGlyphs.UnderB + OiaGlyphs.BoxHuman,
        ConnectionState.ConnectedTn3270E => OiaGlyphs.Box4 + OiaGlyphs.UnderB + OiaGlyphs.BoxSolid,
        _ => OiaGlyphs.Box4 + " " + OiaGlyphs.BoxQuestion,
    };

    /// <summary>The words behind the mode field: the connection sentence, then the full terminal type the way a 3270
    /// user writes one (3278 or 3279 is the colour distinction), which the OIA has no cell for.</summary>
    public static string ModeTip(ConnectionState state, SessionProfile profile) =>
        $"{Connection(state, profile.Host)}\n{TerminalType.For(profile)}";

    /// <summary>Padlock, mark and tooltip: green check when the certificate was verified, amber ! when it was not,
    /// nothing on a plain connection. Both the mark and the colour carry the verdict, so neither has to alone.</summary>
    public static (string Glyph, string Mark, string Tip) Tls(TlsInfo? tls) => tls is { Secure: true }
        ? tls.Verified == true
            ? (PadlockGlyph, VerifiedMark, "TLS, certificate verified")
            : (PadlockGlyph, UnverifiedMark, "TLS, certificate not verified")
        : ("", "", "");

    /// <summary>The message area. Until a session is up it belongs to the connection, as in x3270: the lock, the
    /// broken wire, the step in brackets. Once connected it is the keyboard's: blank when free, otherwise the lock
    /// and x3270's symbol for why. Operator errors are the ones x3270 paints red.</summary>
    public static OiaMessage Message(ConnectionState state, KeyboardStatus status, string host)
    {
        if (!state.IsConnected())
        {
            var text = state switch
            {
                ConnectionState.Disconnected => Locked(OiaGlyphs.NoConnection),
                ConnectionState.Reconnecting => Locked(OiaGlyphs.NoConnection + " " + OiaGlyphs.Clock),
                ConnectionState.Resolving => Locked(OiaGlyphs.NoConnection + " [DNS]"),
                ConnectionState.TcpPending => Locked(OiaGlyphs.NoConnection + " [TCP]"),
                ConnectionState.TlsPending or ConnectionState.TlsPasswordPending => Locked(OiaGlyphs.NoConnection + " [TLS]"),
                ConnectionState.ProxyPending => Locked(OiaGlyphs.NoConnection + " [Proxy]"),
                ConnectionState.TelnetPending => Locked("[TELNET]"),
                _ => Locked(state.ToString()),
            };
            return new OiaMessage(text, false, Connection(state, host));
        }
        var (symbol, isError) = status.Lock switch
        {
            KeyboardLock.Unlocked => ((string?)null, false),
            KeyboardLock.NotConnected => (OiaGlyphs.NoConnection, false),
            KeyboardLock.WaitingForHost => ("SYSTEM", false),
            KeyboardLock.TerminalWait => (OiaGlyphs.Clock, false),
            KeyboardLock.Deferred => ("", false),
            KeyboardLock.MinusFunction => ("-f", true),
            KeyboardLock.ProtectedField => (OiaGlyphs.LeftArrow + OiaGlyphs.Human + OiaGlyphs.RightArrow, true),
            KeyboardLock.NumericOnly => (OiaGlyphs.Human + "NUM", true),
            KeyboardLock.Overflow => (OiaGlyphs.Human + ">", true),
            KeyboardLock.Dbcs => ("<S>", true),
            KeyboardLock.Scrolled => ("Scrolled", false),
            KeyboardLock.Disabled => (OiaGlyphs.KeyLeft + OiaGlyphs.KeyRight, true),
            KeyboardLock.FieldWait => ("[Field]", false),
            KeyboardLock.FileTransfer => ("File Transfer", false),
            _ => (status.LockDetail ?? "Locked", false),
        };
        return new OiaMessage(symbol is null ? "" : Locked(symbol), isError, Keyboard(status));
    }

    private static string Locked(string symbol) => symbol.Length == 0 ? OiaGlyphs.Lock : OiaGlyphs.Lock + " " + symbol;

    /// <summary>The keyboard state in words, for the message area's tooltip.</summary>
    public static string Keyboard(KeyboardStatus status) => status.Lock switch
    {
        KeyboardLock.Unlocked => "Ready",
        KeyboardLock.NotConnected => "Not connected",
        KeyboardLock.WaitingForHost => "Waiting for host",
        KeyboardLock.TerminalWait => "Please wait",
        KeyboardLock.Deferred => "Waiting for host",
        KeyboardLock.MinusFunction => "Not available here, press Esc",
        KeyboardLock.ProtectedField => "Protected field, press Esc",
        KeyboardLock.NumericOnly => "Numbers only here, press Esc",
        KeyboardLock.Overflow => "Field is full, press Esc",
        KeyboardLock.Dbcs => "Invalid double-byte input, press Esc",
        KeyboardLock.Scrolled => "Scrolled back",
        KeyboardLock.Disabled => "Keyboard disabled",
        KeyboardLock.FieldWait => "Waiting for field",
        KeyboardLock.FileTransfer => "File transfer in progress",
        _ => status.LockDetail ?? "Locked",
    };

    public static string Insert(bool on) => on ? OiaGlyphs.Insert : "";

    /// <summary>The LU name the host assigned, bare, as x3270 draws it.</summary>
    public static string Lu(string? luName) => luName ?? "";

    /// <summary>Row and column, one-based, in x3270's rrr/ccc form.</summary>
    public static string Cursor(CursorPosition cursor) => $"{cursor.Row + 1:D3}/{cursor.Column + 1:D3}";

    public static string Fault(BackendFault fault)
    {
        var tail = string.Join(" | ", fault.StderrTail.TakeLast(3));
        var code = fault.ExitCode?.ToString() ?? "unknown";
        var detail = tail.Length > 0 ? $" Last output: {tail}." : "";
        return $"{fault.Message} (exit code {code}).{detail} Turn on Help > Wire Log and reproduce to capture a log.";
    }

    /// <summary>The attempt never completed. An open socket with no 3270 session is as far as the engine's own
    /// reports go: b3270 sends nothing saying why, so a plain connect to a TLS listener and a host that accepted
    /// the socket and then stopped talking look identical from here. The line therefore states what was observed
    /// and offers TLS as one explanation, rather than diagnosing it — following a confident TLS instruction on a
    /// host that does not speak it turns a timeout into an outright failure.</summary>
    public static string ConnectTimeout(SessionProfile profile, TimeSpan timeout, bool socketOpened)
    {
        var text = $"Connection to {profile.Host}:{profile.Port} timed out after {timeout.TotalSeconds:0} seconds.";
        if (!socketOpened) return text;
        text += " The host accepted the connection but never started a 3270 session.";
        return profile.UseTls ? text : text + " If that port expects TLS, turn it on in the profile.";
    }

    public static string WireLog(bool active) => active ? "● wire log" : "";

    /// <param name="overrideOrigin">What pointed at an Override binary, named by the app (today the environment variable).</param>
    public static string Engine(EngineInfo engine, string overrideOrigin)
    {
        // A binary that was never located has no version and no provenance to report; saying it is bundled would
        // point the one user who opens About at the app instead of at whatever override actually broke.
        if (engine.Source == EngineSource.Unknown) return $"{engine.Name}, not found";
        var name = engine.Version is null ? $"{engine.Name}, not started" : $"{engine.Name} {engine.Version}";
        return engine.Source == EngineSource.Bundled ? $"{name}, bundled" : $"{name}, from {overrideOrigin}";
    }
}
