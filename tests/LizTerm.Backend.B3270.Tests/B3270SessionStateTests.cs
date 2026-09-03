using System.Text;
using System.Text.RegularExpressions;
using LizTerm.Backend.B3270.Tests.Fakes;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Tests;

public class B3270SessionStateTests
{
    private static readonly SessionProfile Profile = new() { Name = "t", Host = "h", Port = 23, UseTls = true, VerifyCertificate = false };

    private static async Task<(B3270Session Session, FakeB3270Process Fake)> StartAsync()
    {
        var fake = new FakeB3270Process();
        var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        return (session, fake);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Timed out waiting for " + what);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Screen_mode_resizes_and_publishes()
    {
        var (session, fake) = await StartAsync();
        ScreenSnapshot? published = null;
        session.ScreenUpdated += (_, s) => published = s;
        fake.Emit("""{"screen-mode":{"model":4,"rows":43,"columns":80,"color":true,"oversize":false,"extended":true}}""");
        await WaitUntilAsync(() => published?.Rows == 43, "resize");
        Assert.NotNull(published);
        Assert.Equal(43, published!.Rows);
    }

    [Fact]
    public async Task Screen_changes_apply_text_attributes_and_cursor()
    {
        var (session, fake) = await StartAsync();
        fake.Emit("""{"screen":{"cursor":{"enabled":true,"row":2,"column":9},"rows":[{"row":1,"changes":[{"column":2,"fg":"red","count":1},{"column":3,"fg":"neutralBlack","bg":"red","text":"___"},{"column":6,"fg":"neutralBlack","bg":"red","count":2}]},{"row":2,"changes":[{"column":2,"text":"Field:"},{"column":8,"fg":"red","gr":"underline","count":3}]}]}}""");
        await WaitUntilAsync(() => session.CurrentScreen.GetText(1, 1, 6) == "Field:", "screen text");
        var s = session.CurrentScreen;
        Assert.Equal(new CursorPosition(1, 8, true), s.Cursor);
        Assert.Equal(HostColor.Red, s[0, 1].Foreground);
        Assert.Equal(new System.Text.Rune(' '), s[0, 1].Character);
        Assert.Equal("___", s.GetText(0, 2, 3));
        Assert.Equal(HostColor.Red, s[0, 3].Background);
        Assert.Equal(HostColor.NeutralBlack, s[0, 3].Foreground);
        Assert.Equal(HostColor.Red, s[0, 6].Background);
        Assert.Equal(HostColor.Blue, s[1, 1].Foreground);
        Assert.Equal(CellRendition.Underline, s[1, 8].Rendition);
        Assert.Equal(HostColor.Red, s[1, 9].Foreground);
        Assert.Equal(CellRendition.None, s[1, 10].Rendition);
    }

    [Fact]
    public async Task Cursor_only_screen_hides_cursor()
    {
        var (session, fake) = await StartAsync();
        fake.Emit("""{"screen":{"cursor":{"enabled":true,"row":1,"column":1}}}""");
        await WaitUntilAsync(() => session.CurrentScreen.Cursor.Visible, "cursor on");
        fake.Emit("""{"screen":{"cursor":{"enabled":false}}}""");
        await WaitUntilAsync(() => !session.CurrentScreen.Cursor.Visible, "cursor off");
        Assert.Equal(new CursorPosition(0, 0, false), session.CurrentScreen.Cursor);
    }

    [Fact]
    public async Task Erase_blanks_with_given_colors()
    {
        var (session, fake) = await StartAsync();
        fake.Emit("""{"screen":{"rows":[{"row":1,"changes":[{"column":1,"text":"abc"}]}]}}""");
        await WaitUntilAsync(() => session.CurrentScreen.GetText(0, 0, 3) == "abc", "text");
        fake.Emit("""{"erase":{"logical-rows":24,"logical-columns":80,"fg":"blue","bg":"neutralBlack"}}""");
        await WaitUntilAsync(() => session.CurrentScreen.GetText(0, 0, 3) == "   ", "erase");
        Assert.Equal(HostColor.Blue, session.CurrentScreen[0, 0].Foreground);
    }

    [Theory]
    [InlineData("""{"oia":{"field":"lock","value":"syswait"}}""", KeyboardLock.WaitingForHost)]
    [InlineData("""{"oia":{"field":"lock","value":"oerr protected"}}""", KeyboardLock.ProtectedField)]
    [InlineData("""{"oia":{"field":"lock","value":"oerr numeric"}}""", KeyboardLock.NumericOnly)]
    [InlineData("""{"oia":{"field":"lock","value":"scrolled 3"}}""", KeyboardLock.Scrolled)]
    [InlineData("""{"oia":{"field":"lock","value":"twait"}}""", KeyboardLock.TerminalWait)]
    [InlineData("""{"oia":{"field":"lock","value":"field"}}""", KeyboardLock.FieldWait)]
    [InlineData("""{"oia":{"field":"lock","value":"something-new"}}""", KeyboardLock.Unknown)]
    [InlineData("""{"oia":{"field":"lock"}}""", KeyboardLock.Unlocked)]
    public async Task Oia_lock_maps_to_keyboard_lock(string line, KeyboardLock expected)
    {
        var (session, fake) = await StartAsync();
        KeyboardStatus? status = null;
        session.StatusChanged += (_, s) => status = s;
        fake.Emit(line);
        await WaitUntilAsync(() => session.KeyboardStatus.Lock == expected, "lock " + expected);
        Assert.NotNull(status);
    }

    [Fact]
    public async Task Oia_insert_typeahead_and_lu()
    {
        var (session, fake) = await StartAsync();
        fake.Emit("""{"oia":{"field":"insert","value":true}}""");
        fake.Emit("""{"oia":{"field":"typeahead","value":true}}""");
        fake.Emit("""{"oia":{"field":"lu","value":"IBM0TEQO"}}""");
        await WaitUntilAsync(() => session.KeyboardStatus.LuName == "IBM0TEQO", "lu");
        Assert.True(session.KeyboardStatus.InsertMode);
        Assert.True(session.KeyboardStatus.Typeahead);
        fake.Emit("""{"oia":{"field":"lu"}}""");
        await WaitUntilAsync(() => session.KeyboardStatus.LuName is null, "lu cleared");
    }

    [Fact]
    public async Task Connection_and_tls_map_and_reset()
    {
        var (session, fake) = await StartAsync();
        var states = new List<ConnectionState>();
        session.ConnectionChanged += (_, s) => states.Add(s);
        fake.Emit("""{"connection":{"state":"tcp-pending","host":"h","cause":"ui"}}""");
        fake.Emit("""{"connection":{"state":"tls-pending","host":"h","cause":"ui"}}""");
        fake.Emit("""{"tls":{"secure":true,"verified":false,"session":"Version: TLSv1.3","host-cert":"CN = h"}}""");
        fake.Emit("""{"connection":{"state":"connected-tn3270e","host":"h","cause":"ui"}}""");
        await WaitUntilAsync(() => session.ConnectionState == ConnectionState.ConnectedTn3270E, "connected");
        Assert.Equal([ConnectionState.TcpPending, ConnectionState.TlsPending, ConnectionState.ConnectedTn3270E], states);
        Assert.True(session.Tls!.Secure);
        Assert.False(session.Tls.Verified);
        fake.Emit("""{"connection":{"state":"not-connected"}}""");
        await WaitUntilAsync(() => session.ConnectionState == ConnectionState.Disconnected, "disconnected");
        Assert.Null(session.Tls);
    }

    [Fact]
    public async Task Popup_raises_host_message()
    {
        var (session, fake) = await StartAsync();
        string? message = null;
        session.HostMessage += (_, m) => message = m;
        fake.Emit("""{"popup":{"type":"connection-error","text":"Host unreachable","retrying":false}}""");
        await WaitUntilAsync(() => message == "Host unreachable", "popup");
    }

    [Fact]
    public async Task Connect_sets_verify_then_connects_with_host_string()
    {
        var (session, fake) = await StartAsync();
        await session.ConnectAsync(TestContext.Current.CancellationToken);
        var lines = fake.InputLines;
        Assert.Contains(lines, l => l.Contains("\"Set\"") && l.Contains("\"verifyHostCert\",\"false\""));
        Assert.Contains(lines, l => l.Contains("\"Connect\"") && l.Contains("\"L:h:23\""));
    }

    [Fact]
    public async Task Connect_failure_throws_connection_failed()
    {
        var fake = new FakeB3270Process();
        fake.RunResponder = line =>
        {
            var tag = System.Text.RegularExpressions.Regex.Match(line, "\"r-tag\":\"([^\"]+)\"").Groups[1].Value;
            return line.Contains("\"Connect\"")
                ? [$$$"""{"run-result":{"r-tag":"{{{tag}}}","success":false,"text":["Connection failed:","h/23:","Connection refused"],"text-err":[true,true,true],"time":0.01}}"""]
                : [$$$"""{"run-result":{"r-tag":"{{{tag}}}","success":true,"time":0}}"""];
        };
        var session = new B3270Session(Profile, () => fake);
        var ex = await Assert.ThrowsAsync<ConnectionFailedException>(() => session.ConnectAsync(TestContext.Current.CancellationToken));
        Assert.Equal(["Connection failed:", "h/23:", "Connection refused"], ex.Lines);
    }

    [Fact]
    public async Task Actions_map_to_expected_lines()
    {
        var (session, fake) = await StartAsync();
        await session.SendKeyAsync(TerminalKey.PF3);
        await session.TypeTextAsync(@"a\b");
        await session.PasteTextAsync("line1\nline2");
        await session.MoveCursorAsync(4, 10);
        await session.DisconnectAsync();
        var lines = fake.InputLines;
        Assert.Contains(lines, l => l.Contains("""{"action":"PF","args":["3"]}"""));
        Assert.Contains(lines, l => l.Contains("""{"action":"String","args":["a\\\\b"]}"""));

        // PasteString takes hexadecimal UTF-8, not literal text; assert the round trip rather
        // than a specific encoding.
        var pasteLine = Assert.Single(lines, l => l.Contains("\"action\":\"PasteString\""));
        var hexArg = Regex.Match(pasteLine, "\"PasteString\",\"args\":\\[\"([0-9A-Fa-f]+)\"\\]").Groups[1].Value;
        Assert.Equal("line1\nline2", Encoding.UTF8.GetString(Convert.FromHexString(hexArg)));

        Assert.Contains(lines, l => l.Contains("""{"action":"MoveCursor","args":["4","10"]}"""));
        Assert.Contains(lines, l => l.Contains("""{"action":"Disconnect"}"""));
    }
}
