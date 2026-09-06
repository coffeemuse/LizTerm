using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using LizTerm.Backend.B3270.Tests.Fakes;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Tests;

public class B3270SessionConnectTests
{
    private static readonly SessionProfile Verifying = new() { Name = "t", Host = "h", Port = 4270, UseTls = true, VerifyCertificate = true };

    private static string Tag(string line) => Regex.Match(line, "\"r-tag\":\"([^\"]+)\"").Groups[1].Value;
    private static string Ok(string line) => $$$"""{"run-result":{"r-tag":"{{{Tag(line)}}}","success":true,"time":0}}""";
    private static string Failed(string tag, params string[] text) =>
        $$$"""{"run-result":{"r-tag":"{{{tag}}}","success":false,"text":[{{{string.Join(",", text.Select(t => "\"" + t + "\""))}}}],"time":0}}""";

    private static readonly CertificatePin Pin = new("8C:13:6A:01", "CN=localhost",
        "-----BEGIN CERTIFICATE-----\nZmFrZQ==\n-----END CERTIFICATE-----\n");
    private static readonly SessionProfile Pinned = Verifying with { PinnedCertificate = Pin };

    private static string LastSetLine(FakeB3270Process fake) => fake.InputLines.Last(l => l.Contains("\"Set\""));

    /// <summary>One action argument as it appears on the wire, quotes included. A Windows pin path's backslashes
    /// are escaped there (<c>C:\\Users\\...</c>), so an assertion on the path has to escape them the same way.
    /// RunOperation.Serialize writes with the same relaxed encoder.</summary>
    private static string WireArg(string value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    [Fact]
    public void Tls_settings_are_explicit_for_all_three_cases()
    {
        Assert.Equal(["verifyHostCert", "true", "caFile", "/tmp/x.pem", "acceptHostname", "any"], B3270Session.TlsSettings(true, "/tmp/x.pem", acceptAnyName: true).Args);
        Assert.Equal(["verifyHostCert", "true", "caFile", "/tmp/x.pem", "acceptHostname", ""], B3270Session.TlsSettings(true, "/tmp/x.pem", acceptAnyName: false).Args);
        Assert.Equal(["verifyHostCert", "true", "caFile", "", "acceptHostname", ""], B3270Session.TlsSettings(true, null, acceptAnyName: true).Args);
        Assert.Equal(["verifyHostCert", "false", "caFile", "", "acceptHostname", ""], B3270Session.TlsSettings(false, null, acceptAnyName: false).Args);
        Assert.Equal("Set", B3270Session.TlsSettings(true, null, acceptAnyName: false).Name);
    }

    /// <summary>OpenSSL trusts every certificate in caFile, so a pin that carries the host's CA trusts everything
    /// that CA issued; the engine's own name check is what keeps that to this host, and only a pin that is one
    /// self-signed certificate may switch it off.</summary>
    [Fact]
    public async Task A_pin_with_a_ca_in_it_keeps_the_engine_name_check()
    {
        var fake = new FakeB3270Process();
        var chain = new CertificatePin("8C:13:6A:01", "CN=mvs.lan",
            "-----BEGIN CERTIFICATE-----\nbGVhZg==\n-----END CERTIFICATE-----\n-----BEGIN CERTIFICATE-----\ncm9vdA==\n-----END CERTIFICATE-----\n");
        await using var session = new B3270Session(Verifying with { PinnedCertificate = chain }, () => fake);
        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        var set = LastSetLine(fake);
        Assert.Contains($"\"caFile\",{WireArg(session.LastPinFile!)}", set);
        Assert.Contains("\"acceptHostname\",\"\"", set);
    }

    [Fact]
    public async Task A_pinned_profile_verifies_against_a_temp_file_that_lives_only_for_the_connect_run()
    {
        var fake = new FakeB3270Process();
        string? contentDuringConnect = null;
        var existedDuringConnect = false;
        var ownerOnly = true;
        await using var session = new B3270Session(Pinned, () => fake);
        fake.RunResponder = line =>
        {
            if (line.Contains("\"Connect\""))
            {
                var path = session.LastPinFile!;
                existedDuringConnect = File.Exists(path);
                contentDuringConnect = File.ReadAllText(path);
                if (!OperatingSystem.IsWindows()) ownerOnly = File.GetUnixFileMode(path) == (UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            return [Ok(line)];
        };

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        var set = LastSetLine(fake);
        Assert.Contains("\"verifyHostCert\",\"true\"", set);
        Assert.Contains($"\"caFile\",{WireArg(session.LastPinFile!)}", set);
        Assert.Contains("\"acceptHostname\",\"any\"", set);
        Assert.True(existedDuringConnect, "the pin file did not exist while the Connect run was pending");
        Assert.Equal(Pin.Pem, contentDuringConnect);
        Assert.True(ownerOnly, "the pin file is not owner-only");
        Assert.False(File.Exists(session.LastPinFile), "the pin file outlived the Connect run");
        Assert.StartsWith(Path.GetTempPath(), session.LastPinFile);
        Assert.StartsWith("lizterm-pin-", Path.GetFileName(session.LastPinFile!));
        Assert.EndsWith(".pem", session.LastPinFile);
    }

    [Fact]
    public async Task Verification_off_ignores_the_pin_and_clears_the_trust_settings()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Pinned, () => fake);
        await session.ConnectAsync(new ConnectOptions(VerifyCertificate: false), TestContext.Current.CancellationToken);
        Assert.Contains("\"verifyHostCert\",\"false\",\"caFile\",\"\",\"acceptHostname\",\"\"", LastSetLine(fake));
        Assert.Null(session.LastPinFile);
    }

    [Fact]
    public async Task An_unpinned_verifying_profile_clears_the_trust_settings()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Verifying, () => fake);
        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("\"verifyHostCert\",\"true\",\"caFile\",\"\",\"acceptHostname\",\"\"", LastSetLine(fake));
        Assert.Null(session.LastPinFile);
    }

    [Fact]
    public async Task A_one_shot_pin_wins_over_the_profile_pin()
    {
        var fake = new FakeB3270Process();
        var oneShot = new CertificatePin("00:11", "CN=new", "-----BEGIN CERTIFICATE-----\nbmV3\n-----END CERTIFICATE-----\n");
        string? content = null;
        await using var session = new B3270Session(Pinned, () => fake);
        fake.RunResponder = line =>
        {
            if (line.Contains("\"Connect\"")) content = File.ReadAllText(session.LastPinFile!);
            return [Ok(line)];
        };
        await session.ConnectAsync(new ConnectOptions(Pin: oneShot), TestContext.Current.CancellationToken);
        Assert.Equal(oneShot.Pem, content);
    }

    [Fact]
    public async Task The_pin_file_is_deleted_after_a_failed_connect_and_after_engine_death()
    {
        var failing = new FakeB3270Process
        {
            RunResponder = line => line.Contains("\"Connect\"")
                ? [Failed(Tag(line), "Connection failed:", "TLS: Host certificate verification failed:", "self-signed certificate (18)")]
                : [Ok(line)],
        };
        await using (var session = new B3270Session(Pinned, () => failing))
        {
            await Assert.ThrowsAsync<ConnectionFailedException>(() => session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.NotNull(session.LastPinFile);
            Assert.False(File.Exists(session.LastPinFile));
        }

        var dying = new FakeB3270Process();
        dying.RunResponder = line =>
        {
            if (line.Contains("\"Connect\"")) { dying.Exit(1); return []; }
            return [Ok(line)];
        };
        await using (var session = new B3270Session(Pinned, () => dying))
        {
            await Assert.ThrowsAsync<BackendUnavailableException>(() => session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.NotNull(session.LastPinFile);
            Assert.False(File.Exists(session.LastPinFile));
        }
    }

    [Fact]
    public async Task The_pin_file_is_deleted_after_a_cancelled_connect()
    {
        string? connectTag = null;
        var fake = new FakeB3270Process();
        fake.RunResponder = line =>
        {
            if (line.Contains("\"Connect\"")) { connectTag = Tag(line); return []; }
            if (line.Contains("\"Disconnect\""))
                return [Ok(line), Failed(connectTag!, "Connection failed"), """{"connection":{"state":"not-connected"}}"""];
            return [Ok(line)];
        };
        await using var session = new B3270Session(Pinned, () => fake);
        using var cts = new CancellationTokenSource();
        var connect = session.ConnectAsync(cancellationToken: cts.Token);
        await fake.WaitForInputAsync(l => l.Contains("\"Connect\""));
        Assert.True(File.Exists(session.LastPinFile));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);
        Assert.False(File.Exists(session.LastPinFile));
    }

    [Fact]
    public async Task Options_override_the_profile_verify_setting()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Verifying, () => fake);
        await session.ConnectAsync(new ConnectOptions(VerifyCertificate: false), TestContext.Current.CancellationToken);
        Assert.Contains(fake.InputLines, l => l.Contains("\"verifyHostCert\",\"false\""));

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(fake.InputLines, l => l.Contains("\"verifyHostCert\",\"true\""));
    }

    [Fact]
    public async Task Certificate_failure_sets_the_flag_and_other_failures_do_not()
    {
        var fake = new FakeB3270Process
        {
            RunResponder = line => line.Contains("\"Connect\"")
                ? [Failed(Tag(line), "Connection failed:", "TLS: Host certificate verification failed:", "self-signed certificate (18)")]
                : [Ok(line)],
        };
        await using var session = new B3270Session(Verifying, () => fake);
        var ex = await Assert.ThrowsAsync<ConnectionFailedException>(() => session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(ex.CertificateVerificationFailed);
        Assert.Equal("self-signed certificate (18)", ex.Lines[^1]);

        fake.RunResponder = line => line.Contains("\"Connect\"") ? [Failed(Tag(line), "Connection failed:", "Connection refused")] : [Ok(line)];
        var refused = await Assert.ThrowsAsync<ConnectionFailedException>(() => session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(refused.CertificateVerificationFailed);
    }

    [Fact]
    public async Task Cancel_during_a_pending_connect_sends_disconnect_and_throws_cancellation()
    {
        string? connectTag = null;
        var fake = new FakeB3270Process();
        fake.RunResponder = line =>
        {
            if (line.Contains("\"Connect\"")) { connectTag = Tag(line); return []; }
            if (line.Contains("\"Disconnect\""))
                return [Ok(line), Failed(connectTag!, "Connection failed"), """{"connection":{"state":"not-connected"}}"""];
            return [Ok(line)];
        };
        await using var session = new B3270Session(Verifying, () => fake);
        using var cts = new CancellationTokenSource();

        var attempt = session.ConnectAsync(cancellationToken: cts.Token);
        await fake.WaitForInputAsync(l => l.Contains("\"Connect\""));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt);
        Assert.Equal(1, fake.InputLines.Count(l => l.Contains("\"Disconnect\"")));
        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);

        // The same process connects again.
        fake.RunResponder = null;
        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, fake.InputLines.Count(l => l.Contains("\"Connect\"")));
    }

    [Fact]
    public async Task Cancelled_before_connect_throws_without_sending_connect()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Verifying, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ConnectAsync(cancellationToken: cts.Token));
        Assert.DoesNotContain(fake.InputLines, l => l.Contains("\"Connect\""));
    }

    [Fact]
    public async Task Cancel_waits_for_the_disconnect_to_be_answered_and_sends_exactly_one()
    {
        string? connectTag = null;
        string? disconnectLine = null;
        var fake = new FakeB3270Process();
        fake.RunResponder = line =>
        {
            if (line.Contains("\"Connect\"")) { connectTag = Tag(line); return []; }
            if (line.Contains("\"Disconnect\""))
            {
                // Fail the Connect and drop the line, but do not answer the Disconnect yet.
                disconnectLine = line;
                return [Failed(connectTag!, "Connection failed"), """{"connection":{"state":"not-connected"}}"""];
            }
            return [Ok(line)];
        };
        await using var session = new B3270Session(Verifying, () => fake);
        using var cts = new CancellationTokenSource();

        var attempt = session.ConnectAsync(cancellationToken: cts.Token);
        await fake.WaitForInputAsync(l => l.Contains("\"Connect\""));
        cts.Cancel();
        await fake.WaitForInputAsync(l => l.Contains("\"Disconnect\""));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(attempt.IsCompleted, "the attempt must wait for the Disconnect to be answered");

        fake.Emit(Ok(disconnectLine!));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt);
        Assert.Equal(1, fake.InputLines.Count(l => l.Contains("\"Disconnect\"")));
    }

    [Fact]
    public async Task Cancel_that_races_a_successful_connect_still_disconnects_and_reports_cancellation()
    {
        var cts = new CancellationTokenSource();
        var fake = new FakeB3270Process();
        fake.RunResponder = line =>
        {
            if (line.Contains("\"Connect\""))
            {
                // Cancel on the writer's thread before the success is even emitted.
                cts.Cancel();
                return [Ok(line), """{"connection":{"state":"connected-3270","host":"h","cause":"ui"}}"""];
            }
            if (line.Contains("\"Disconnect\"")) return [Ok(line), """{"connection":{"state":"not-connected"}}"""];
            return [Ok(line)];
        };
        await using var session = new B3270Session(Verifying, () => fake);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ConnectAsync(cancellationToken: cts.Token));
        Assert.Equal(1, fake.InputLines.Count(l => l.Contains("\"Disconnect\"")));
        cts.Dispose();
    }

    [Fact]
    public async Task Cancelled_cold_start_tears_the_process_down_and_the_next_connect_starts_fresh()
    {
        var first = new FakeB3270Process { AutoInitialize = false };
        var second = new FakeB3270Process();
        var processes = new Queue<FakeB3270Process>([first, second]);
        await using var session = new B3270Session(Verifying, () => processes.Dequeue());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ConnectAsync(cancellationToken: cts.Token));
        Assert.True(first.Started);
        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(second.Started);
        Assert.Contains(second.InputLines, l => l.Contains("\"Connect\""));
    }

    [Fact]
    public async Task A_failed_connect_waits_for_the_engine_to_report_not_connected()
    {
        var fake = new FakeB3270Process();
        fake.RunResponder = line => line.Contains("\"Connect\"")
            ? [Failed(Tag(line), "Connection failed:", "Connection refused")]  // no not-connected yet
            : [Ok(line)];
        await using var session = new B3270Session(Verifying, () => fake) { DisconnectTimeout = TimeSpan.FromSeconds(2) };
        await session.StartProcessAsync(CancellationToken.None);
        fake.Emit("""{"connection":{"state":"tcp-pending","host":"h","cause":"ui"}}""");
        await Wait.UntilAsync(() => session.ConnectionState == ConnectionState.TcpPending, "tcp-pending");

        var attempt = session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(attempt.IsCompleted, "the attempt must wait for not-connected");

        fake.Emit("""{"connection":{"state":"not-connected"}}""");
        await Assert.ThrowsAsync<ConnectionFailedException>(() => attempt);
        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
    }

    [Fact]
    public async Task A_failed_connect_gives_up_waiting_after_the_disconnect_timeout()
    {
        var fake = new FakeB3270Process();
        fake.RunResponder = line => line.Contains("\"Connect\"")
            ? [Failed(Tag(line), "Connection failed:", "Connection refused")]
            : [Ok(line)];
        await using var session = new B3270Session(Verifying, () => fake) { DisconnectTimeout = TimeSpan.FromMilliseconds(100) };
        await session.StartProcessAsync(CancellationToken.None);
        fake.Emit("""{"connection":{"state":"tcp-pending","host":"h","cause":"ui"}}""");
        await Wait.UntilAsync(() => session.ConnectionState == ConnectionState.TcpPending, "tcp-pending");

        await Assert.ThrowsAsync<ConnectionFailedException>(() => session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ConnectionState.TcpPending, session.ConnectionState);
    }

    /// <summary>Regression: the cancel's Disconnect run was awaited with no bound, so an engine that accepted it
    /// and never answered left ConnectAsync pending past its own cancellation, with nothing to unwedge it.</summary>
    [Fact]
    public async Task A_cancel_gives_up_on_an_unanswered_disconnect_instead_of_hanging()
    {
        // Answers the verify setting, then nothing: neither the Connect nor the cancel's Disconnect.
        var fake = new FakeB3270Process { RunResponder = line => line.Contains("\"Set\"") ? [Ok(line)] : [] };
        await using var session = new B3270Session(Verifying, () => fake) { DisconnectTimeout = TimeSpan.FromMilliseconds(200) };
        using var cts = new CancellationTokenSource();

        var attempt = session.ConnectAsync(cancellationToken: cts.Token);
        await fake.WaitForInputAsync(l => l.Contains("\"Connect\""));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    /// <summary>Regression: the verify setting was awaited with no bound and observed no token, so a wedged engine
    /// held the attempt open before the Connect had even gone out.</summary>
    [Fact]
    public async Task A_cancel_while_the_verify_setting_is_unanswered_ends_the_attempt()
    {
        var fake = new FakeB3270Process { RunResponder = _ => [] };
        await using var session = new B3270Session(Verifying, () => fake) { DisconnectTimeout = TimeSpan.FromMilliseconds(200) };
        using var cts = new CancellationTokenSource();

        var attempt = session.ConnectAsync(cancellationToken: cts.Token);
        await fake.WaitForInputAsync(l => l.Contains("verifyHostCert"));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.DoesNotContain(fake.InputLines, l => l.Contains("\"Connect\""));
    }

    /// <summary>Regression: the cancel's Disconnect was awaited only where the Connect run returned normally, so a
    /// Connect that faulted (the engine dying mid-cancel) let it outlive the attempt and land on the next one.
    /// TransferAsync already awaits its cancel in a finally for the same reason.</summary>
    [Fact]
    public async Task A_cancel_whose_connect_run_faults_still_waits_for_its_disconnect()
    {
        var fake = new FakeB3270Process { RunResponder = line => line.Contains("\"Set\"") ? [Ok(line)] : [] };
        await using var session = new B3270Session(Verifying, () => fake) { DisconnectTimeout = TimeSpan.FromMilliseconds(200) };
        using var cts = new CancellationTokenSource();

        var attempt = session.ConnectAsync(cancellationToken: cts.Token);
        await fake.WaitForInputAsync(l => l.Contains("\"Connect\""));

        // Hold the cancel's Disconnect inside its write, then kill the engine so the pending Connect run faults.
        var reached = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        fake.BeforeWrite = () => { reached.Set(); release.Wait(TimeSpan.FromSeconds(5)); };
        try
        {
            await cts.CancelAsync();
            Assert.True(reached.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "the cancel never sent a Disconnect");
            fake.Exit(1);

            var settled = await Task.WhenAny(attempt, Task.Delay(500, TestContext.Current.CancellationToken));
            Assert.NotSame(attempt, settled);
        }
        finally
        {
            // Always release: the blocked write holds the session's write lock, and disposal needs it.
            release.Set();
        }
        await Assert.ThrowsAnyAsync<Exception>(() => attempt.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    /// <summary>Regression: the wait slot was a single field, so a second caller's install dropped the first
    /// caller's completion source and the first waited out the whole DisconnectTimeout.</summary>
    [Fact]
    public async Task Two_overlapping_disconnect_waits_both_end_on_the_one_not_connected_report()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Verifying, () => fake);
        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        fake.Emit("""{"connection":{"state":"connected-3270","host":"h","cause":"ui"}}""");
        await Wait.UntilAsync(() => session.ConnectionState == ConnectionState.Connected3270, "connected");

        var first = session.DisconnectAsync();
        var second = session.DisconnectAsync();
        await fake.WaitForInputAsync(l => l.Contains("\"Disconnect\""));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        fake.Emit("""{"connection":{"state":"not-connected"}}""");
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    /// <summary>The completion source belongs to the connection state, not to the first waiter: one that gives up
    /// after its timeout must not take the source with it, or a later report would find nothing to complete and a
    /// waiter that joined late would sit out its own full timeout beside an already closed connection.</summary>
    [Fact]
    public async Task A_report_after_one_waiter_timed_out_still_ends_a_later_waiter_at_once()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Verifying, () => fake) { DisconnectTimeout = TimeSpan.FromMilliseconds(600) };
        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        fake.Emit("""{"connection":{"state":"connected-3270","host":"h","cause":"ui"}}""");
        await Wait.UntilAsync(() => session.ConnectionState == ConnectionState.Connected3270, "connected");

        var first = session.DisconnectAsync();
        await Task.Delay(450, TestContext.Current.CancellationToken);
        var second = session.DisconnectAsync();
        await first.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.False(second.IsCompleted);

        fake.Emit("""{"connection":{"state":"not-connected"}}""");
        await second.WaitAsync(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
    }
}
