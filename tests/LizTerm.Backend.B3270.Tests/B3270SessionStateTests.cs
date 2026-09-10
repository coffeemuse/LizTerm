// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

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

    private const string NotConnected = """{"connection":{"state":"not-connected"}}""";

    private static string RunResult(string inputLine)
    {
        var tag = Regex.Match(inputLine, "\"r-tag\":\"([^\"]+)\"").Groups[1].Value;
        return $$$"""{"run-result":{"r-tag":"{{{tag}}}","success":true,"time":0}}""";
    }

    [Fact]
    public async Task Screen_mode_resizes_and_publishes()
    {
        var (session, fake) = await StartAsync();
        ScreenSnapshot? published = null;
        // The reader thread writes this; Volatile pairs the release with the test thread's acquire read below.
        session.ScreenUpdated += (_, s) => Volatile.Write(ref published, s);
        fake.Emit("""{"screen-mode":{"model":4,"rows":43,"columns":80,"color":true,"oversize":false,"extended":true}}""");
        await Wait.UntilAsync(() => Volatile.Read(ref published)?.Rows == 43, "resize");
        Assert.Equal(43, Volatile.Read(ref published)!.Rows);
    }

    [Fact]
    public async Task Screen_changes_apply_text_attributes_and_cursor()
    {
        var (session, fake) = await StartAsync();
        fake.Emit("""{"screen":{"cursor":{"enabled":true,"row":2,"column":9},"rows":[{"row":1,"changes":[{"column":2,"fg":"red","count":1},{"column":3,"fg":"neutralBlack","bg":"red","text":"___"},{"column":6,"fg":"neutralBlack","bg":"red","count":2}]},{"row":2,"changes":[{"column":2,"text":"Field:"},{"column":8,"fg":"red","gr":"underline","count":3}]}]}}""");
        await Wait.UntilAsync(() => session.CurrentScreen.GetText(1, 1, 6) == "Field:", "screen text");
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
        await Wait.UntilAsync(() => session.CurrentScreen.Cursor.Visible, "cursor on");
        fake.Emit("""{"screen":{"cursor":{"enabled":false}}}""");
        await Wait.UntilAsync(() => !session.CurrentScreen.Cursor.Visible, "cursor off");
        Assert.Equal(new CursorPosition(0, 0, false), session.CurrentScreen.Cursor);
    }

    [Fact]
    public async Task Erase_blanks_with_given_colors()
    {
        var (session, fake) = await StartAsync();
        fake.Emit("""{"screen":{"rows":[{"row":1,"changes":[{"column":1,"text":"abc"}]}]}}""");
        await Wait.UntilAsync(() => session.CurrentScreen.GetText(0, 0, 3) == "abc", "text");
        fake.Emit("""{"erase":{"logical-rows":24,"logical-columns":80,"fg":"blue","bg":"neutralBlack"}}""");
        await Wait.UntilAsync(() => session.CurrentScreen.GetText(0, 0, 3) == "   ", "erase");
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
        // The reader thread publishes the property before it raises the event, so waiting on the property and
        // then asserting on the event's capture races that gap. Wait on the event's own value instead: by the
        // time it is visible, the property is too.
        session.StatusChanged += (_, s) => Volatile.Write(ref status, s);
        fake.Emit(line);
        await Wait.UntilAsync(() => Volatile.Read(ref status)?.Lock == expected, "lock " + expected);
        Assert.Equal(expected, session.KeyboardStatus.Lock);
    }

    [Fact]
    public async Task Oia_insert_typeahead_and_lu()
    {
        var (session, fake) = await StartAsync();
        fake.Emit("""{"oia":{"field":"insert","value":true}}""");
        fake.Emit("""{"oia":{"field":"typeahead","value":true}}""");
        fake.Emit("""{"oia":{"field":"lu","value":"IBM0TEQO"}}""");
        await Wait.UntilAsync(() => session.KeyboardStatus.LuName == "IBM0TEQO", "lu");
        Assert.True(session.KeyboardStatus.InsertMode);
        Assert.True(session.KeyboardStatus.Typeahead);
        fake.Emit("""{"oia":{"field":"lu"}}""");
        await Wait.UntilAsync(() => session.KeyboardStatus.LuName is null, "lu cleared");
    }

    [Fact]
    public async Task Connection_and_tls_map_and_reset()
    {
        var (session, fake) = await StartAsync();
        // SetConnectionState assigns the property before it raises the event, so waiting on the property and then
        // reading the list races the last Add. Wait on the list itself, and guard it: the reader thread appends
        // while this thread reads, which a bare List<T> does not survive.
        var states = new List<ConnectionState>();
        var statesLock = new object();
        List<ConnectionState> States() { lock (statesLock) return [.. states]; }
        session.ConnectionChanged += (_, s) => { lock (statesLock) states.Add(s); };
        fake.Emit("""{"connection":{"state":"tcp-pending","host":"h","cause":"ui"}}""");
        fake.Emit("""{"connection":{"state":"tls-pending","host":"h","cause":"ui"}}""");
        fake.Emit("""{"tls":{"secure":true,"verified":false,"session":"Version: TLSv1.3","host-cert":"CN = h"}}""");
        fake.Emit("""{"connection":{"state":"connected-tn3270e","host":"h","cause":"ui"}}""");
        await Wait.UntilAsync(() => States().Count == 3, "three connection states");
        Assert.Equal([ConnectionState.TcpPending, ConnectionState.TlsPending, ConnectionState.ConnectedTn3270E], States());
        Assert.Equal(ConnectionState.ConnectedTn3270E, session.ConnectionState);
        Assert.True(session.Tls!.Secure);
        Assert.False(session.Tls.Verified);
        fake.Emit("""{"connection":{"state":"not-connected"}}""");
        await Wait.UntilAsync(() => session.ConnectionState == ConnectionState.Disconnected, "disconnected");
        Assert.Null(session.Tls);
    }

    [Fact]
    public async Task Popup_raises_host_message()
    {
        var (session, fake) = await StartAsync();
        string? message = null;
        // The reader thread writes this; Volatile pairs the release with the test thread's acquire read below.
        session.HostMessage += (_, m) => Volatile.Write(ref message, m);
        fake.Emit("""{"popup":{"type":"connection-error","text":"Host unreachable","retrying":false}}""");
        await Wait.UntilAsync(() => Volatile.Read(ref message) == "Host unreachable", "popup");
    }

    [Fact]
    public async Task Connect_sets_verify_then_connects_with_host_string()
    {
        var (session, fake) = await StartAsync();
        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
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
        var ex = await Assert.ThrowsAsync<ConnectionFailedException>(() => session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken));
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
        fake.Emit("""{"connection":{"state":"connected-3270","host":"h","cause":"ui"}}""");
        await Wait.UntilAsync(() => session.ConnectionState == ConnectionState.Connected3270, "connected");
        fake.RunResponder = line => line.Contains("\"Disconnect\"") ? [NotConnected, RunResult(line)] : [RunResult(line)];
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

    /// <summary>Holds only because <see cref="Profile"/> leaves AutoReconnect at its false default (task 13
    /// review, finding 4): with it on, DisarmReconnectAsync deliberately runs before this early-out, so a
    /// Disconnect on an already-down session would still write a Set(reconnect,false) line. Turning AutoReconnect
    /// on in this fixture would make the assertion below fail -- that is the disarm working as designed, not a
    /// regression in it.</summary>
    [Fact]
    public async Task DisconnectAsync_sends_nothing_when_not_connected()
    {
        var (session, fake) = await StartAsync();
        await session.DisconnectAsync();
        Assert.DoesNotContain(fake.InputLines, l => l.Contains("\"Disconnect\""));
        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
    }

    [Fact]
    public async Task DisconnectAsync_returns_only_after_b3270_reports_not_connected()
    {
        var (session, fake) = await StartAsync();
        fake.Emit("""{"connection":{"state":"connected-3270","host":"h","cause":"ui"}}""");
        await Wait.UntilAsync(() => session.ConnectionState == ConnectionState.Connected3270, "connected");
        // b3270 acknowledges the action at once; the connection indication arrives a little later.
        fake.RunResponder = line => [RunResult(line)];

        var disconnect = session.DisconnectAsync();
        await fake.WaitForInputAsync(l => l.Contains("\"Disconnect\""));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(disconnect.IsCompleted, "DisconnectAsync completed before the state changed");

        fake.Emit(NotConnected);
        await disconnect.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
    }

    [Fact]
    public async Task DisconnectAsync_gives_up_after_the_timeout_when_no_state_arrives()
    {
        var (session, fake) = await StartAsync();
        session.DisconnectTimeout = TimeSpan.FromMilliseconds(200);
        fake.Emit("""{"connection":{"state":"connected-3270","host":"h","cause":"ui"}}""");
        await Wait.UntilAsync(() => session.ConnectionState == ConnectionState.Connected3270, "connected");
        fake.RunResponder = line => [RunResult(line)];   // acknowledged, but never reports not-connected

        await session.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Contains(fake.InputLines, l => l.Contains("\"Disconnect\""));
        Assert.Equal(ConnectionState.Connected3270, session.ConnectionState);
    }
}
