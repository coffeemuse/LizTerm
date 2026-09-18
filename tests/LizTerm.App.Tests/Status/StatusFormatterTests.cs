// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Status;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Status;

public class StatusFormatterTests
{
    [Theory]
    [InlineData(ConnectionState.Disconnected, "Not connected")]
    [InlineData(ConnectionState.Resolving, "Looking up mvs.local")]
    [InlineData(ConnectionState.TcpPending, "Connecting to mvs.local")]
    [InlineData(ConnectionState.TlsPending, "Securing connection to mvs.local")]
    [InlineData(ConnectionState.TelnetPending, "Negotiating with mvs.local")]
    [InlineData(ConnectionState.Connected3270, "Connected to mvs.local (TN3270)")]
    [InlineData(ConnectionState.ConnectedTn3270E, "Connected to mvs.local (TN3270E)")]
    [InlineData(ConnectionState.ConnectedNvt, "Connected to mvs.local (NVT)")]
    public void Connection_text_is_plain_language(ConnectionState state, string expected) =>
        Assert.Equal(expected, StatusFormatter.Connection(state, "mvs.local"));

    // The mode field is x3270's: the boxed 4, then A or B underlined for TN3270 or TN3270E, then what the
    // session is (solid box: bound; boxed ?: none; N: NVT; the boxed human: SSCP-LU).
    [Theory]
    [InlineData(ConnectionState.Disconnected, "\uE195 \uE189")]
    [InlineData(ConnectionState.TcpPending, "\uE195 \uE189")]
    [InlineData(ConnectionState.TelnetPending, "\uE195 \uE189")]
    [InlineData(ConnectionState.ConnectedNvt, "\uE195\uE196N")]
    [InlineData(ConnectionState.ConnectedNvtCharMode, "\uE195\uE196N")]
    [InlineData(ConnectionState.Connected3270, "\uE195\uE196\uE18A")]
    [InlineData(ConnectionState.ConnectedUnbound, "\uE195\uE187\uE189")]
    [InlineData(ConnectionState.ConnectedENvt, "\uE195\uE187N")]
    [InlineData(ConnectionState.ConnectedSscp, "\uE195\uE187\uE198")]
    [InlineData(ConnectionState.ConnectedTn3270E, "\uE195\uE187\uE18A")]
    public void Mode_field_is_the_x3270_one(ConnectionState state, string expected) =>
        Assert.Equal(expected, StatusFormatter.Mode(state));

    [Fact]
    public void Mode_tooltip_carries_the_connection_sentence_and_the_model()
    {
        var profile = new SessionProfile { Name = "a", Host = "mvs.local", Model = 4 };
        Assert.Equal("Connected to mvs.local (TN3270E)\n3279-4-E", StatusFormatter.ModeTip(ConnectionState.ConnectedTn3270E, profile));
        Assert.Equal("Not connected\n3279-4-E", StatusFormatter.ModeTip(ConnectionState.Disconnected, profile));
        Assert.Equal("Not connected\n3278-4-E", StatusFormatter.ModeTip(ConnectionState.Disconnected, profile with { Display = TerminalDisplay.Mono }));
    }

    [Fact]
    public void Tls_is_a_padlock_and_a_mark_with_the_words_on_the_tooltip()
    {
        Assert.Equal(("", "", ""), StatusFormatter.Tls(null));
        Assert.Equal(("", "", ""), StatusFormatter.Tls(new TlsInfo(false, null, null, null)));
        Assert.Equal(("\uE0A2", "✓", "TLS, certificate verified"), StatusFormatter.Tls(new TlsInfo(true, true, null, null)));
        Assert.Equal(("\uE0A2", "!", "TLS, certificate not verified"), StatusFormatter.Tls(new TlsInfo(true, false, null, null)));
        Assert.Equal(("\uE0A2", "!", "TLS, certificate not verified"), StatusFormatter.Tls(new TlsInfo(true, null, null, null)));
    }

    // While no session is bound the message area belongs to the connection, as in x3270: the lock, the broken wire,
    // and the step in brackets. The tooltip is the plain sentence. TN3270E unbound is x3270's [TN3270E] step even
    // though b3270 calls the keyboard not connected there (the test passes a keyboard reason it must ignore).
    [Theory]
    [InlineData(ConnectionState.Disconnected, "\uE191 \uE18C\uE18B\uE18C\uE18D\uE18E", "Not connected")]
    [InlineData(ConnectionState.Reconnecting, "\uE191 \uE18C\uE18B\uE18C\uE18D\uE18E \uE18F\uE190", "Reconnecting to mvs.local")]
    [InlineData(ConnectionState.Resolving, "\uE191 \uE18C\uE18B\uE18C\uE18D\uE18E [DNS]", "Looking up mvs.local")]
    [InlineData(ConnectionState.TcpPending, "\uE191 \uE18C\uE18B\uE18C\uE18D\uE18E [TCP]", "Connecting to mvs.local")]
    [InlineData(ConnectionState.TlsPending, "\uE191 \uE18C\uE18B\uE18C\uE18D\uE18E [TLS]", "Securing connection to mvs.local")]
    [InlineData(ConnectionState.TlsPasswordPending, "\uE191 \uE18C\uE18B\uE18C\uE18D\uE18E [TLS]", "Waiting for TLS key password")]
    [InlineData(ConnectionState.ProxyPending, "\uE191 \uE18C\uE18B\uE18C\uE18D\uE18E [Proxy]", "Connecting to mvs.local through proxy")]
    [InlineData(ConnectionState.TelnetPending, "\uE191 [TELNET]", "Negotiating with mvs.local")]
    [InlineData(ConnectionState.ConnectedUnbound, "\uE191 [TN3270E]", "Connected to mvs.local (TN3270E, unbound)")]
    public void Message_area_follows_the_connection_until_a_session_is_bound(ConnectionState state, string text, string tip)
    {
        var locked = KeyboardStatus.Initial with { Lock = KeyboardLock.ProtectedField };
        Assert.Equal(new OiaMessage(text, false, tip), StatusFormatter.Message(state, locked, "mvs.local"));
    }

    // Once connected it is the keyboard's: blank when free, otherwise the lock and x3270's symbol for why.
    // Operator errors are the red ones.
    [Theory]
    [InlineData(KeyboardLock.Unlocked, "", false, "Ready")]
    [InlineData(KeyboardLock.NotConnected, "\uE191 \uE18C\uE18B\uE18C\uE18D\uE18E", false, "Not connected")]
    [InlineData(KeyboardLock.WaitingForHost, "\uE191 SYSTEM", false, "Waiting for host")]
    [InlineData(KeyboardLock.TerminalWait, "\uE191 \uE18F\uE190", false, "Please wait")]
    [InlineData(KeyboardLock.Deferred, "\uE191", false, "Waiting for host")]
    [InlineData(KeyboardLock.MinusFunction, "\uE191 -f", true, "Not available here, press Esc")]
    [InlineData(KeyboardLock.ProtectedField, "\uE191 \uE192\uE186\uE184", true, "Protected field, press Esc")]
    [InlineData(KeyboardLock.NumericOnly, "\uE191 \uE186NUM", true, "Numbers only here, press Esc")]
    [InlineData(KeyboardLock.Overflow, "\uE191 \uE186>", true, "Field is full, press Esc")]
    [InlineData(KeyboardLock.Dbcs, "\uE191 <S>", true, "Invalid double-byte input, press Esc")]
    [InlineData(KeyboardLock.Scrolled, "\uE191 Scrolled", false, "Scrolled back")]
    [InlineData(KeyboardLock.Disabled, "\uE191 \uE193\uE194", true, "Keyboard disabled")]
    [InlineData(KeyboardLock.FieldWait, "\uE191 [Field]", false, "Waiting for field")]
    [InlineData(KeyboardLock.FileTransfer, "\uE191 File Transfer", false, "File transfer in progress")]
    public void Message_area_is_the_keyboards_once_connected(KeyboardLock lockState, string text, bool isError, string tip)
    {
        var status = KeyboardStatus.Initial with { Lock = lockState };
        Assert.Equal(new OiaMessage(text, isError, tip), StatusFormatter.Message(ConnectionState.ConnectedTn3270E, status, "mvs.local"));
    }

    [Fact]
    public void Unknown_lock_shows_its_detail()
    {
        var status = KeyboardStatus.Initial with { Lock = KeyboardLock.Unknown, LockDetail = "weird-state" };
        Assert.Equal(new OiaMessage("\uE191 weird-state", false, "weird-state"),
            StatusFormatter.Message(ConnectionState.Connected3270, status, "mvs.local"));
    }

    [Fact]
    public void Cursor_is_one_based_in_x3270s_three_digit_form() =>
        Assert.Equal("021/013", StatusFormatter.Cursor(new CursorPosition(20, 12, true)));

    [Fact]
    public void Insert_is_the_caret_glyph()
    {
        Assert.Equal("\uE181", StatusFormatter.Insert(true));
        Assert.Equal("", StatusFormatter.Insert(false));
    }

    [Fact]
    public void Lu_name_stands_bare()
    {
        Assert.Equal("", StatusFormatter.Lu(null));
        Assert.Equal("IBM0TEQO", StatusFormatter.Lu("IBM0TEQO"));
    }

    [Fact]
    public void Fault_text_mentions_exit_code_and_stderr()
    {
        var text = StatusFormatter.Fault(new BackendFault("The emulator engine (b3270) exited unexpectedly.", ["a", "b", "c", "d"], 137));
        Assert.Contains("exit code 137", text);
        Assert.Contains("b | c | d", text);
        Assert.EndsWith("Turn on Help > Wire Log and reproduce to capture a log.", text);
    }

    [Fact]
    public void Connect_timeout_states_what_was_observed_and_offers_tls_only_as_a_possibility()
    {
        var plain = new SessionProfile { Name = "p", Host = "mvs.local", Port = 4270 };
        Assert.Equal("Connection to mvs.local:4270 timed out after 30 seconds. The host accepted the connection but never started a 3270 session. If that port expects TLS, turn it on in the profile.",
            StatusFormatter.ConnectTimeout(plain, TimeSpan.FromSeconds(30), socketOpened: true));
        Assert.Equal("Connection to mvs.local:4270 timed out after 30 seconds.",
            StatusFormatter.ConnectTimeout(plain, TimeSpan.FromSeconds(30), socketOpened: false));
        Assert.Equal("Connection to mvs.local:4270 timed out after 5 seconds. The host accepted the connection but never started a 3270 session.",
            StatusFormatter.ConnectTimeout(plain with { UseTls = true }, TimeSpan.FromSeconds(5), socketOpened: true));
    }

    [Fact]
    public void Wire_log_indicator()
    {
        Assert.Equal("● wire log", StatusFormatter.WireLog(true));
        Assert.Equal("", StatusFormatter.WireLog(false));
    }

    [Fact]
    public void Engine_line_names_version_and_source()
    {
        var bundled = new EngineInfo("b3270", "4.5.6 (b3270 v4.5ga6)", "/app/b3270", EngineSource.Bundled);
        Assert.Equal("b3270 4.5.6 (b3270 v4.5ga6), bundled", StatusFormatter.Engine(bundled, "LIZTERM_B3270_PATH"));
        var overridden = bundled with { Source = EngineSource.Override };
        Assert.Equal("b3270 4.5.6 (b3270 v4.5ga6), from LIZTERM_B3270_PATH", StatusFormatter.Engine(overridden, "LIZTERM_B3270_PATH"));
        Assert.Equal("b3270, not started, bundled", StatusFormatter.Engine(bundled with { Version = null }, "LIZTERM_B3270_PATH"));
        Assert.Equal("b3270, not started, from LIZTERM_B3270_PATH", StatusFormatter.Engine(overridden with { Version = null }, "LIZTERM_B3270_PATH"));
        // Never located: claiming a provenance it does not have is what misleads the user who opened About.
        Assert.Equal("b3270, not found",
            StatusFormatter.Engine(new EngineInfo("b3270", null, "", EngineSource.Unknown), "LIZTERM_B3270_PATH"));
    }
}
