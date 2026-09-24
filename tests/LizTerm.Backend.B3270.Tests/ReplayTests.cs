// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.RegularExpressions;
using LizTerm.Backend.B3270.Tests.Fakes;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Tests;

public class ReplayTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public async Task Ibmlink_help_screen_replays_to_expected_state()
    {
        var fake = new FakeB3270Process { AutoInitialize = false, RunResponder = _ => [] };
        foreach (var line in File.ReadLines(Fixture("ibmlink-help.jsonl"))) fake.Emit(line);
        fake.Exit(0);

        var session = new B3270Session(new SessionProfile { Name = "replay", Host = "127.0.0.1" }, () => fake);
        var states = new List<ConnectionState>();
        var screens = 0;
        var bells = 0;
        session.ConnectionChanged += (_, s) => states.Add(s);
        session.ScreenUpdated += (_, _) => screens++;
        session.BellRang += (_, _) => bells++;
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, _) => ended.TrySetResult();

        await session.StartProcessAsync(TestContext.Current.CancellationToken);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var screen = session.CurrentScreen;
        Assert.Equal(24, screen.Rows);
        Assert.Equal(80, screen.Columns);
        Assert.Equal("SVM0201P", screen.GetText(0, 1, 8));
        Assert.Equal("SYSTEM: IBM0SM23", screen.GetText(1, 1, 16));
        // The recording ends with Quit, after which b3270 hides the cursor but its position is kept.
        Assert.Equal(new CursorPosition(20, 12, false), screen.Cursor);
        Assert.Equal(HostColor.NeutralWhite, screen[6, 0].Foreground);
        Assert.True(screen[6, 0].Rendition.HasFlag(CellRendition.Highlight));
        // This host negotiates TN3270E from the first exchange (see the recorded "cstate" lines in
        // the original .trc), so the session never passes through the plain Connected3270 state --
        // it goes straight to ConnectedUnbound/ConnectedTn3270E once the BIND completes.
        Assert.Contains(ConnectionState.ConnectedTn3270E, states);
        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
        Assert.True(screens > 0);
        // Line 30 of the fixture is {"bell":{}}: a real host rang it, and this is the end-to-end proof it reaches
        // the session's subscribers (#47).
        Assert.Equal(1, bells);
    }

    /// <summary>A 3278 session as b3270 reports it (#123): intensity arrives, colour never does. Every cell ends
    /// the replay in the fill the resize put there, which is the backend's business to leave alone and the
    /// renderer's to ignore. A cell with any other colour would mean b3270 had started sending fg for a mono
    /// model, and the App's phosphor rule would then be hiding information.</summary>
    [Fact]
    public async Task A_mono_3278_replay_paints_intensity_and_never_a_colour()
    {
        var fake = new FakeB3270Process { AutoInitialize = false, RunResponder = _ => [] };
        foreach (var line in File.ReadLines(Fixture("mono-3278-opening.jsonl"))) fake.Emit(line);
        fake.Exit(0);

        var profile = new SessionProfile { Name = "replay", Host = "mvsce.test", Display = TerminalDisplay.Mono };
        var session = new B3270Session(profile, () => fake);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, _) => ended.TrySetResult();

        await session.StartProcessAsync(TestContext.Current.CancellationToken);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var screen = session.CurrentScreen;
        Assert.Equal(24, screen.Rows);
        Assert.Equal(80, screen.Columns);
        // The Hercules banner is repainted as the MVS/CE logo before the recording ends; Quit then hides the cursor.
        Assert.Equal("ver: 6672504", screen.GetText(0, 67, 12));
        Assert.Equal(new CursorPosition(22, 16, false), screen.Cursor);
        var fill = screen[0, 0].Foreground;
        var highlighted = 0;
        for (var row = 0; row < screen.Rows; row++)
            for (var col = 0; col < screen.Columns; col++)
            {
                Assert.Equal(fill, screen[row, col].Foreground);
                Assert.Equal(HostColor.NeutralBlack, screen[row, col].Background);
                if (screen[row, col].Rendition.HasFlag(CellRendition.Highlight)) highlighted++;
            }
        Assert.True(highlighted > 0, "the host still intensifies fields on a 3278");
    }

    [Fact]
    public async Task Gateway_login_replays_tls_connect_tab_and_host_disconnect()
    {
        var fake = new FakeB3270Process { AutoInitialize = false, RunResponder = _ => [] };
        foreach (var line in File.ReadLines(Fixture("gateway-login-tls.jsonl"))) fake.Emit(line);
        fake.Exit(0);

        var profile = new SessionProfile { Name = "replay", Host = "gateway.test", Port = 4270, UseTls = true, VerifyCertificate = false };
        var session = new B3270Session(profile, () => fake);
        var states = new List<ConnectionState>();
        TlsInfo? tlsWhileConnected = null;
        ScreenSnapshot? firstLoginScreen = null;
        var locks = new List<KeyboardLock>();
        session.ConnectionChanged += (_, s) =>
        {
            states.Add(s);
            if (s == ConnectionState.Connected3270) tlsWhileConnected = session.Tls;
        };
        session.ScreenUpdated += (_, s) =>
        {
            if (firstLoginScreen is null && s.GetText(0, 0, 80).Contains("TN3270 GATEWAY LOGIN")) firstLoginScreen = s;
        };
        session.StatusChanged += (_, k) => locks.Add(k.Lock);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, _) => ended.TrySetResult();

        await session.StartProcessAsync(TestContext.Current.CancellationToken);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Plain TLS tunnel to a host that never negotiates TN3270E: the second TelnetPending is the
        // telnet negotiation resuming inside the tunnel once the handshake is done.
        Assert.Equal(
            [ConnectionState.TcpPending, ConnectionState.TelnetPending, ConnectionState.TlsPending,
             ConnectionState.TelnetPending, ConnectionState.Connected3270, ConnectionState.Disconnected],
            states);

        Assert.NotNull(tlsWhileConnected);
        Assert.True(tlsWhileConnected!.Secure);
        Assert.False(tlsWhileConnected.Verified);
        Assert.Contains("tn3270proxy quick-start", tlsWhileConnected.HostCertificate);
        Assert.Null(session.Tls);

        // First rendering of the login screen: cursor parked in the User ID field.
        Assert.NotNull(firstLoginScreen);
        Assert.Equal(new CursorPosition(22, 16, true), firstLoginScreen!.Cursor);
        Assert.Equal(HostColor.Turquoise, firstLoginScreen[22, 3].Foreground);
        Assert.Equal(HostColor.Green, firstLoginScreen[22, 16].Foreground);
        Assert.True(firstLoginScreen[22, 16].Rendition.HasFlag(CellRendition.Underline));

        // Tab moved to the Password field; PF3 made the host drop the line, which hides the cursor
        // but keeps the last screen and the cursor's position.
        var final = session.CurrentScreen;
        Assert.Equal(new CursorPosition(22, 45, false), final.Cursor);
        Assert.Contains("TN3270 GATEWAY LOGIN", final.GetText(0, 0, 80));
        Assert.Contains(KeyboardLock.Unlocked, locks);
        Assert.Equal(KeyboardLock.NotConnected, session.KeyboardStatus.Lock);
        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
    }

    /// <summary>Recorded from the live pinning test: a connect with verifyHostCert on and caFile pointing at the
    /// gateway's own certificate. The only difference from gateway-login-tls.jsonl that matters is verified:true.</summary>
    [Fact]
    public async Task Gateway_pinned_login_replays_to_a_verified_tls_connection()
    {
        var fake = new FakeB3270Process { AutoInitialize = false, RunResponder = _ => [] };
        foreach (var line in File.ReadLines(Fixture("gateway-pinned-login.jsonl"))) fake.Emit(line);
        fake.Exit(0);

        var profile = new SessionProfile { Name = "replay", Host = "gateway.test", Port = 4270, UseTls = true };
        var session = new B3270Session(profile, () => fake);
        var states = new List<ConnectionState>();
        TlsInfo? tlsWhileConnected = null;
        session.ConnectionChanged += (_, s) =>
        {
            states.Add(s);
            if (s == ConnectionState.Connected3270) tlsWhileConnected = session.Tls;
        };
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, _) => ended.TrySetResult();

        await session.StartProcessAsync(TestContext.Current.CancellationToken);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Contains(ConnectionState.Connected3270, states);
        Assert.Equal(ConnectionState.Disconnected, states[^1]);
        Assert.NotNull(tlsWhileConnected);
        Assert.True(tlsWhileConnected!.Secure);
        Assert.True(tlsWhileConnected.Verified);
    }

    /// <summary>The fixture was recorded with run tags "set" and "connect"; the responder replays the engine's
    /// answers against the tags this session actually sends, so ConnectAsync sees the real failure text.</summary>
    [Fact]
    public async Task Gateway_certificate_failure_replays_to_a_flagged_connection_failure()
    {
        var lines = File.ReadAllLines(Fixture("gateway-cert-failure.jsonl"));
        var fake = new FakeB3270Process { AutoInitialize = false, RunResponder = input => Respond(input, lines) };
        fake.Emit(lines[0]);

        var profile = new SessionProfile { Name = "replay", Host = "gateway.test", Port = 4270, UseTls = true, VerifyCertificate = true };
        await using var session = new B3270Session(profile, () => fake);
        var states = new List<ConnectionState>();
        session.ConnectionChanged += (_, s) => states.Add(s);

        var ex = await Assert.ThrowsAsync<ConnectionFailedException>(() => session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(ex.CertificateVerificationFailed);
        Assert.Contains("self-signed certificate", ex.Lines[^1]);
        Assert.Equal([ConnectionState.TcpPending, ConnectionState.TelnetPending, ConnectionState.TlsPending, ConnectionState.Disconnected], states);
        Assert.Null(session.Tls);
    }

    /// <summary>#170 as the reporter saw it. The engine answered the Connect run with <c>"time":0,039</c> — its
    /// desktop's locale writes decimals with a comma — so the one line that would have completed the attempt is not
    /// JSON and is dropped, while the connection indications and the screen around it are fine. The session is
    /// therefore connected and painted, and the attempt still runs out its token: a live screen under a timeout
    /// banner. The fixture is real engine output and stays that way; the fix lives in the engine's environment
    /// (B3270ChildProcess.ConfigureLocale), proved against the real binary by the integration project.</summary>
    [Fact]
    public async Task A_comma_decimal_connect_answer_leaves_a_live_screen_and_a_timed_out_attempt()
    {
        var lines = File.ReadAllLines(Fixture("locale-comma-connect.jsonl"));
        var fake = new FakeB3270Process
        {
            RunResponder = input =>
            {
                var tag = Regex.Match(input, "\"r-tag\":\"([^\"]+)\"").Groups[1].Value;
                // The Set that precedes every Connect was answered in the log too (tag 43), instantly, with an
                // integer time -- which is why that one parsed.
                if (input.Contains("\"Set\"")) return ["{\"run-result\":{\"r-tag\":\"" + tag + "\",\"success\":true,\"time\":0}}"];
                if (input.Contains("\"Connect\"")) return lines.TakeWhile(l => !l.Contains("not-connected")).Select(l => l.Replace("\"r-tag\":\"44\"", $"\"r-tag\":\"{tag}\"")).ToList();
                if (input.Contains("\"Disconnect\"")) return lines.SkipWhile(l => !l.Contains("not-connected")).Select(l => l.Replace("\"r-tag\":\"86\"", $"\"r-tag\":\"{tag}\"")).ToList();
                return [];
            },
        };

        var profile = new SessionProfile { Name = "replay", Host = "hercules.test" };
        // The Disconnect's answer in the fixture is malformed too, so the cancel would otherwise wait out the whole
        // DisconnectTimeout before this test could end; the app did exactly that, five seconds long.
        await using var session = new B3270Session(profile, () => fake) { DisconnectTimeout = TimeSpan.FromMilliseconds(200) };
        var states = new List<ConnectionState>();
        session.ConnectionChanged += (_, s) => states.Add(s);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ConnectAsync(cancellationToken: cts.Token));

        Assert.Contains(ConnectionState.Connected3270, states);
        Assert.Equal("Hercules Version  :", session.CurrentScreen.GetText(0, 1, 19));
        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
    }

    private static IReadOnlyList<string> Respond(string input, string[] fixture)
    {
        var tag = Regex.Match(input, "\"r-tag\":\"([^\"]+)\"").Groups[1].Value;
        if (input.Contains("\"Set\""))
            return fixture.Where(l => l.Contains("\"r-tag\":\"set\"")).Select(l => l.Replace("\"r-tag\":\"set\"", $"\"r-tag\":\"{tag}\"")).ToList();
        if (input.Contains("\"Connect\""))
            return fixture.SkipWhile(l => !l.Contains("connect-attempt")).Select(l => l.Replace("\"r-tag\":\"connect\"", $"\"r-tag\":\"{tag}\"")).ToList();
        return [];
    }

    /// <summary>#30's one unproven claim: an oversize geometry reaches the buffer end-to-end, so the UI needs
    /// no change. Every other fixture is a model geometry, so nothing established that until this one. This
    /// fixture's own `initialize` block carries both a `screen-mode` and an `erase` indication with the same
    /// oversize `100x50`, and `B3270Session` resizes on either one independently (`screen-mode` unconditionally,
    /// `erase` whenever its logical dimensions disagree with the buffer's) -- so this test pins the outcome
    /// both indications produce, not which of the two produced it. Trimming `erase` out to isolate `screen-mode`
    /// would make the fixture no longer real recorded engine output, which is a worse trade.</summary>
    [Fact]
    public async Task An_oversize_geometry_resizes_the_buffer()
    {
        var fake = new FakeB3270Process { AutoInitialize = false, RunResponder = _ => [] };
        foreach (var line in File.ReadLines(Fixture("oversize-100x50.jsonl"))) fake.Emit(line);
        fake.Exit(0);

        var profile = new SessionProfile { Name = "replay", Host = "127.0.0.1", Oversize = "100x50" };
        var session = new B3270Session(profile, () => fake);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, _) => ended.TrySetResult();

        await session.StartProcessAsync(TestContext.Current.CancellationToken);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var screen = session.CurrentScreen;
        Assert.Equal(50, screen.Rows);
        Assert.Equal(100, screen.Columns);
    }

    /// <summary>The trimmed ISPF fixture has no initialize block, so the fake's own starts the session and the fixture
    /// follows. What it proves is the host's behaviour LizTerm relies on: after a transfer from ISPF the host
    /// repaints the panel, with no *** pause. The cause is not in the fixture: the planning spike's data-stream trace
    /// showed IND$FILE's closing message coming through the transfer itself (an FT:MSG structured field), not as TSO
    /// line output.</summary>
    [Fact]
    public async Task Indfile_from_ispf_replays_back_to_the_ispf_panel()
    {
        var fake = new FakeB3270Process();
        var session = new B3270Session(new SessionProfile { Name = "replay", Host = "127.0.0.1" }, () => fake);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, _) => ended.TrySetResult();
        await session.StartProcessAsync(TestContext.Current.CancellationToken);

        foreach (var line in File.ReadLines(Fixture("indfile-ispf-roundtrip.jsonl"))) fake.Emit(line);
        fake.Exit(0);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var text = session.CurrentScreen.ToText();
        Assert.Contains("===>", text);
        Assert.DoesNotContain("***", text);
    }

    /// <summary>A host's X'CA' (#191): b3270 hands it over as U+00AD SOFT HYPHEN, and the buffer keeps it in a cell
    /// of its own, so the columns after it are where the host put them. Drawing it is the App's business
    /// (CellGlyphs.ForDrawing); this pins that the screen model underneath is right and stays the real character.</summary>
    [Fact]
    public async Task A_soft_hyphen_from_the_host_keeps_its_own_cell()
    {
        var fake = new FakeB3270Process();
        var session = new B3270Session(new SessionProfile { Name = "replay", Host = "127.0.0.1" }, () => fake);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, _) => ended.TrySetResult();
        await session.StartProcessAsync(TestContext.Current.CancellationToken);

        foreach (var line in File.ReadLines(Fixture("soft-hyphen-clist.jsonl"))) fake.Emit(line);
        fake.Exit(0);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var screen = session.CurrentScreen;
        Assert.Equal(" AB\u00ADCD FOLLOWS ONE SOFT HYPHEN", screen.GetText(1, 0, 30));
        Assert.Equal(new System.Text.Rune(0xAD), screen[1, 3].Character);
        Assert.Equal(new System.Text.Rune('C'), screen[1, 4].Character);
        Assert.Equal(" \u00AD\u00AD\u00ADXYZ AFTER THREE", screen.GetText(2, 0, 19));
        Assert.Equal(" READY ", screen.GetText(3, 0, 7));
    }
}
