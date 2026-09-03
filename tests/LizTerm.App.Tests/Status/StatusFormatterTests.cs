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
        Assert.Contains("LIZTERM_WIRE_LOG", text);
    }
}
