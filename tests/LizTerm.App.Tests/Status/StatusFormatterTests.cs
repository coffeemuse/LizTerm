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

    [Fact]
    public void Tls_text_uses_padlock_glyph_and_verification()
    {
        Assert.Equal("", StatusFormatter.Tls(null));
        Assert.Equal("", StatusFormatter.Tls(new TlsInfo(false, null, null, null)));
        Assert.Equal("\uE0A2 TLS, certificate verified", StatusFormatter.Tls(new TlsInfo(true, true, null, null)));
        Assert.Equal("\uE0A2 TLS, certificate not verified", StatusFormatter.Tls(new TlsInfo(true, false, null, null)));
    }

    [Theory]
    [InlineData(KeyboardLock.Unlocked, "✓ Ready")]
    [InlineData(KeyboardLock.NotConnected, "✕ Not connected")]
    [InlineData(KeyboardLock.WaitingForHost, "✕ Waiting for host")]
    [InlineData(KeyboardLock.ProtectedField, "✕ Protected field, press Esc")]
    [InlineData(KeyboardLock.NumericOnly, "✕ Numbers only here, press Esc")]
    [InlineData(KeyboardLock.Overflow, "✕ Field is full, press Esc")]
    [InlineData(KeyboardLock.MinusFunction, "✕ Not available here, press Esc")]
    public void Keyboard_text_is_plain_language(KeyboardLock lockState, string expected) =>
        Assert.Equal(expected, StatusFormatter.Keyboard(KeyboardStatus.Initial with { Lock = lockState }));

    [Fact]
    public void Unknown_lock_shows_detail()
    {
        var status = KeyboardStatus.Initial with { Lock = KeyboardLock.Unknown, LockDetail = "weird-state" };
        Assert.Equal("✕ weird-state", StatusFormatter.Keyboard(status));
    }

    [Fact]
    public void Cursor_is_one_based_and_padded() =>
        Assert.Equal("21/013", StatusFormatter.Cursor(new CursorPosition(20, 12, true)));

    [Fact]
    public void Insert_and_model_texts()
    {
        Assert.Equal("INS", StatusFormatter.Insert(true));
        Assert.Equal("", StatusFormatter.Insert(false));
        var profile = new SessionProfile { Name = "a", Host = "h", Model = 4 };
        Assert.Equal("Model 4-E", StatusFormatter.Model(profile, null));
        Assert.Equal("Model 4-E  LU IBM0TEQO", StatusFormatter.Model(profile, "IBM0TEQO"));
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
    }
}
