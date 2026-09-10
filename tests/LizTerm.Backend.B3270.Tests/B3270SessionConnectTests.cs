// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

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

    // Excludes the reconnect arm's own Set: for a profile with AutoReconnect true, "the last Set" would
    // otherwise resolve to `Set reconnect true` rather than the TLS Set this helper exists to find (task 12
    // review, finding 4). No current test enables both at once, but a future TLS test that also turns on
    // auto-reconnect must not silently assert against the wrong line.
    private static string LastSetLine(FakeB3270Process fake) =>
        fake.InputLines.Last(l => l.Contains("\"Set\"") && !l.Contains("\"reconnect\""));

    /// <summary>One action argument as it appears on the wire, quotes included. A Windows pin path's backslashes
    /// are escaped there (<c>C:\\Users\\...</c>), so an assertion on the path has to escape them the same way.
    /// RunOperation.Serialize writes with the same relaxed encoder.</summary>
    private static string WireArg(string value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>THE most important behaviour this task adds (plan 3d task 8): a pin the engine cannot honour must
    /// fail loudly, before a single action reaches the wire, rather than silently connecting with verification
    /// against the platform's own trust store while the user believes their pin is still in force. This uses a
    /// deliberately harder, hypothetical option list -- a provider missing all three gateable toggles, not just
    /// caFile -- to prove the refusal does not depend on which of the other two happen to still be present; see
    /// <see cref="A_pin_against_the_real_schannel_option_set_is_also_refused"/> below for the shape that actually
    /// broke CI, where verifyHostCert and acceptHostname ARE present and only caFile is missing (real behaviour,
    /// verified against x3270 4.5ga6's own Common/Win32/sio_schannel.c and Common/sioc.c).</summary>
    [Fact]
    public async Task A_pin_the_engine_cannot_honour_throws_before_any_action_is_sent()
    {
        var fake = new FakeB3270Process { AutoInitialize = false };
        fake.Emit("""{"initialize":[{"hello":{"version":"4.5.6","build":"win"}},{"tls-hello":{"supported":true,"provider":"Schannel","options":["clientCert","tlsMinProtocol","tlsMaxProtocol"]}}]}""");
        await using var session = new B3270Session(Pinned, () => fake);

        var ex = await Assert.ThrowsAsync<ConnectionFailedException>(
            () => session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            "This engine's TLS provider cannot verify a pinned certificate (it has no caFile option). " +
            "Connecting would silently trust the platform certificate store instead of the pin.",
            ex.Message);
        Assert.False(session.CanPinCertificates);
        Assert.DoesNotContain(fake.InputLines, l => l.Contains("\"Set\""));
        Assert.DoesNotContain(fake.InputLines, l => l.Contains("\"Connect\""));
    }

    /// <summary>The one-shot pin (Connect Anyway's "Trust this certificate") is exactly as loud a failure as a
    /// saved profile pin: nothing about where the pin came from changes what an engine that cannot honour it
    /// would silently downgrade to.</summary>
    [Fact]
    public async Task A_one_shot_pin_the_engine_cannot_honour_also_throws_before_connecting()
    {
        var fake = new FakeB3270Process { AutoInitialize = false };
        fake.Emit("""{"initialize":[{"hello":{"version":"4.5.6","build":"win"}},{"tls-hello":{"supported":true,"provider":"Schannel","options":["clientCert","tlsMinProtocol","tlsMaxProtocol"]}}]}""");
        await using var session = new B3270Session(Verifying, () => fake);
        var oneShot = new CertificatePin("00:11", "CN=new", "-----BEGIN CERTIFICATE-----\nbmV3\n-----END CERTIFICATE-----\n");

        await Assert.ThrowsAsync<ConnectionFailedException>(
            () => session.ConnectAsync(new ConnectOptions(Pin: oneShot), TestContext.Current.CancellationToken));

        Assert.DoesNotContain(fake.InputLines, l => l.Contains("\"Connect\""));
    }

    /// <summary>An engine that cannot pin still connects perfectly well with no pin in force: this is not a
    /// blanket "Windows never verifies" regression, only the pin path is refused. Review finding 1 (plan 3d task 8):
    /// this list is the degenerate case -- an engine that supports NONE of the three gateable toggles, so TlsSettings
    /// sends no Set at all -- and it is legitimate to keep testing on its own, but it must not be the only shape
    /// tested: <see cref="An_engine_with_the_real_schannel_option_set_still_sends_verifyHostCert_and_acceptHostname"/>
    /// below is the real Schannel shape (verifyHostCert and acceptHostname present, only caFile missing), where a
    /// Set very much IS sent. A gate "simplified" to all-or-nothing would still pass every assertion here while
    /// silently breaking that one.</summary>
    [Fact]
    public async Task An_engine_without_caFile_still_connects_when_no_pin_is_in_force()
    {
        var fake = new FakeB3270Process { AutoInitialize = false };
        fake.Emit("""{"initialize":[{"hello":{"version":"4.5.6","build":"win"}},{"tls-hello":{"supported":true,"provider":"Schannel","options":["clientCert","tlsMinProtocol","tlsMaxProtocol"]}}]}""");
        await using var session = new B3270Session(Verifying, () => fake);

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(session.CanPinCertificates);
        Assert.DoesNotContain(fake.InputLines, l => l.Contains("\"Set\""));
        Assert.Contains(fake.InputLines, l => l.Contains("\"Connect\""));
    }

    /// <summary>Spec item 5: when caFile is unsupported and no pin is in force, DecideCaFile must not read the OS
    /// trust store at all (measured 210 ms) or write a file nothing will ever point the engine at -- a Schannel
    /// engine already verifies against the Windows certificate store natively once verifyHostCert is on, which is
    /// the outcome plan 3b engineered for macOS/Linux through caFile in the first place.</summary>
    [Fact]
    public async Task An_engine_without_caFile_reads_no_anchors_and_writes_no_file()
    {
        var fake = new FakeB3270Process { AutoInitialize = false };
        fake.Emit("""{"initialize":[{"hello":{"version":"4.5.6","build":"win"}},{"tls-hello":{"supported":true,"provider":"Schannel","options":["clientCert","tlsMinProtocol","tlsMaxProtocol"]}}]}""");
        var trust = new FakeTrustAnchorSource();
        await using var session = new B3270Session(Verifying, () => fake) { TrustAnchors = trust };

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, trust.Calls);
        Assert.Null(session.LastCaFile);
    }

    /// <summary>Regression guard for macOS/Linux (spec item 3): an engine that DOES list all three toggles keeps
    /// sending all three explicitly on every connect, exactly as before this task.</summary>
    [Fact]
    public async Task An_engine_that_lists_caFile_still_sends_all_three_toggles()
    {
        var fake = new FakeB3270Process { AutoInitialize = false };
        fake.Emit("""{"initialize":[{"hello":{"version":"4.5.6","build":"mac"}},{"tls-hello":{"supported":true,"provider":"OpenSSL 3.6.3","options":["acceptHostname","verifyHostCert","startTls","caDir","caFile","certFile","certFileType","chainFile","keyFile","keyFileType","keyPasswd","tlsMinProtocol","tlsMaxProtocol","tlsSecurityLevel"]}}]}""");
        await using var session = new B3270Session(Verifying, () => fake);

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(session.CanPinCertificates);
        var set = LastSetLine(fake);
        Assert.Contains("\"verifyHostCert\",\"true\"", set);
        Assert.Contains("\"caFile\",\"\"", set);
        Assert.Contains("\"acceptHostname\",\"\"", set);
    }

    /// <summary>Review finding 1 (plan 3d task 8): THE shape that actually broke CI. A real Schannel engine's
    /// tls-hello lists acceptHostname, verifyHostCert and startTls unconditionally -- x3270 4.5ga6's
    /// TLS_REQUIRED_OPTS (include/tls_config.h) unions into sio_all_options_supported() (Common/sioc.c) whenever
    /// TLS is compiled in at all -- and Schannel's own sio_options_supported() (Common/Win32/sio_schannel.c) adds
    /// only clientCert/tlsMinProtocol/tlsMaxProtocol on top. Only caFile is missing, not all three. Every test
    /// above this one uses the all-three-missing degenerate list; a gate "simplified" to
    /// `if (!Supports("caFile")) return null;` would still pass every one of them while silently dropping
    /// verifyHostCert and acceptHostname on this, the real Windows connect.</summary>
    [Fact]
    public async Task An_engine_with_the_real_schannel_option_set_still_sends_verifyHostCert_and_acceptHostname()
    {
        var fake = new FakeB3270Process { AutoInitialize = false };
        fake.Emit("""{"initialize":[{"hello":{"version":"4.5.6","build":"win"}},{"tls-hello":{"supported":true,"provider":"Schannel","options":["acceptHostname","verifyHostCert","startTls","clientCert","tlsMinProtocol","tlsMaxProtocol"]}}]}""");
        await using var session = new B3270Session(Verifying, () => fake);

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(session.CanPinCertificates);
        var set = LastSetLine(fake);
        Assert.Contains("\"verifyHostCert\",\"true\"", set);
        Assert.Contains("\"acceptHostname\",\"\"", set);
        Assert.DoesNotContain("caFile", set);
    }

    /// <summary>Review finding 1's other half: the pin refusal must hold against the real Schannel shape too, not
    /// only against the degenerate all-three-missing list <see cref="A_pin_the_engine_cannot_honour_throws_before_any_action_is_sent"/>
    /// uses. caFile is the one toggle a pin actually needs, and it is the one this real shape lacks.</summary>
    [Fact]
    public async Task A_pin_against_the_real_schannel_option_set_is_also_refused()
    {
        var fake = new FakeB3270Process { AutoInitialize = false };
        fake.Emit("""{"initialize":[{"hello":{"version":"4.5.6","build":"win"}},{"tls-hello":{"supported":true,"provider":"Schannel","options":["acceptHostname","verifyHostCert","startTls","clientCert","tlsMinProtocol","tlsMaxProtocol"]}}]}""");
        await using var session = new B3270Session(Pinned, () => fake);

        await Assert.ThrowsAsync<ConnectionFailedException>(
            () => session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.False(session.CanPinCertificates);
        Assert.DoesNotContain(fake.InputLines, l => l.Contains("\"Set\""));
        Assert.DoesNotContain(fake.InputLines, l => l.Contains("\"Connect\""));
    }

    [Fact]
    public void Tls_settings_are_explicit_for_all_three_cases()
    {
        var all = new[] { "verifyHostCert", "caFile", "acceptHostname" };
        Assert.Equal(["verifyHostCert", "true", "caFile", "/tmp/x.pem", "acceptHostname", "any"], B3270Session.TlsSettings(true, "/tmp/x.pem", acceptAnyName: true, all)!.Args);
        Assert.Equal(["verifyHostCert", "true", "caFile", "/tmp/x.pem", "acceptHostname", ""], B3270Session.TlsSettings(true, "/tmp/x.pem", acceptAnyName: false, all)!.Args);
        Assert.Equal(["verifyHostCert", "true", "caFile", "", "acceptHostname", ""], B3270Session.TlsSettings(true, null, acceptAnyName: true, all)!.Args);
        Assert.Equal(["verifyHostCert", "false", "caFile", "", "acceptHostname", ""], B3270Session.TlsSettings(false, null, acceptAnyName: false, all)!.Args);
        Assert.Equal("Set", B3270Session.TlsSettings(true, null, acceptAnyName: false, all)!.Name);
    }

    /// <summary>Each toggle is gated independently on the engine's own reported option list (spec item 3), not on
    /// the operating system or the provider string. Order in the supported list must not matter, and a supported
    /// list that names none of the three collapses to no action at all rather than a pointless Set() with zero
    /// pairs (which b3270 reads as "show all toggles" -- harmless, but nothing this session wants to send).</summary>
    [Fact]
    public void Tls_settings_gates_each_toggle_independently_on_what_the_engine_lists()
    {
        Assert.Equal(["caFile", "/tmp/x.pem"], B3270Session.TlsSettings(true, "/tmp/x.pem", acceptAnyName: true, ["caFile"])!.Args);
        Assert.Equal(["verifyHostCert", "true"], B3270Session.TlsSettings(true, "/tmp/x.pem", acceptAnyName: true, ["verifyHostCert"])!.Args);
        Assert.Equal(["acceptHostname", "any"], B3270Session.TlsSettings(true, "/tmp/x.pem", acceptAnyName: true, ["acceptHostname"])!.Args);
        Assert.Null(B3270Session.TlsSettings(true, "/tmp/x.pem", acceptAnyName: true, ["clientCert", "tlsMinProtocol", "tlsMaxProtocol"]));
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
        Assert.Contains($"\"caFile\",{WireArg(session.LastCaFile!)}", set);
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
                var path = session.LastCaFile!;
                existedDuringConnect = File.Exists(path);
                contentDuringConnect = File.ReadAllText(path);
                if (!OperatingSystem.IsWindows()) ownerOnly = File.GetUnixFileMode(path) == (UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            return [Ok(line)];
        };

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        var set = LastSetLine(fake);
        Assert.Contains("\"verifyHostCert\",\"true\"", set);
        Assert.Contains($"\"caFile\",{WireArg(session.LastCaFile!)}", set);
        Assert.Contains("\"acceptHostname\",\"any\"", set);
        Assert.True(existedDuringConnect, "the pin file did not exist while the Connect run was pending");
        Assert.Equal(Pin.Pem, contentDuringConnect);
        Assert.True(ownerOnly, "the pin file is not owner-only");
        Assert.False(File.Exists(session.LastCaFile), "the pin file outlived the Connect run");
        Assert.StartsWith(Path.GetTempPath(), session.LastCaFile);
        Assert.StartsWith("lizterm-pin-", Path.GetFileName(session.LastCaFile!));
        Assert.EndsWith(".pem", session.LastCaFile);
    }

    [Fact]
    public async Task Verification_off_ignores_the_pin_and_clears_the_trust_settings()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Pinned, () => fake);
        await session.ConnectAsync(new ConnectOptions(VerifyCertificate: false), TestContext.Current.CancellationToken);
        Assert.Contains("\"verifyHostCert\",\"false\",\"caFile\",\"\",\"acceptHostname\",\"\"", LastSetLine(fake));
        Assert.Null(session.LastCaFile);
    }

    [Fact]
    public async Task An_unpinned_verifying_profile_clears_the_trust_settings()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Verifying, () => fake);
        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("\"verifyHostCert\",\"true\",\"caFile\",\"\",\"acceptHostname\",\"\"", LastSetLine(fake));
        Assert.Null(session.LastCaFile);
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
            if (line.Contains("\"Connect\"")) content = File.ReadAllText(session.LastCaFile!);
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
            Assert.NotNull(session.LastCaFile);
            Assert.False(File.Exists(session.LastCaFile));
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
            Assert.NotNull(session.LastCaFile);
            Assert.False(File.Exists(session.LastCaFile));
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
        Assert.True(File.Exists(session.LastCaFile));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);
        Assert.False(File.Exists(session.LastCaFile));
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
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    /// <summary>The completion source belongs to the connection state, not to the first waiter: one that gives up
    /// after its timeout must not take the source with it, or a later report would find nothing to complete and a
    /// waiter that joined late would sit out its own full timeout beside an already closed connection.</summary>
    [Fact]
    public async Task A_report_after_one_waiter_timed_out_still_ends_a_later_waiter_at_once()
    {
        var fake = new FakeB3270Process();
        // The three spans below are one ratio, not three numbers: `first` gives up at DisconnectTimeout, `second`
        // joins a third of the way in and must still be waiting then, and the report must reach it inside the
        // rest of its own budget. Scaled together for a loaded runner; scale them together again if they pinch.
        await using var session = new B3270Session(Verifying, () => fake) { DisconnectTimeout = TimeSpan.FromMilliseconds(1200) };
        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        fake.Emit("""{"connection":{"state":"connected-3270","host":"h","cause":"ui"}}""");
        await Wait.UntilAsync(() => session.ConnectionState == ConnectionState.Connected3270, "connected");

        var first = session.DisconnectAsync();
        await Task.Delay(400, TestContext.Current.CancellationToken);
        var second = session.DisconnectAsync();
        await first.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(second.IsCompleted);

        fake.Emit("""{"connection":{"state":"not-connected"}}""");
        await second.WaitAsync(TimeSpan.FromMilliseconds(600), TestContext.Current.CancellationToken);
    }

    /// <summary>The row of spec section 3 this whole plan exists for: verifying, no pin, anchors available.</summary>
    [Fact]
    public async Task An_unpinned_verifying_connect_names_a_roots_file_holding_the_trust_anchors()
    {
        var fake = new FakeB3270Process();
        var trust = new FakeTrustAnchorSource();
        string? contentDuringConnect = null;
        var ownerOnly = true;
        string rootsFile;
        await using (var session = new B3270Session(Verifying, () => fake) { TrustAnchors = trust })
        {
            fake.RunResponder = line =>
            {
                if (line.Contains("\"Connect\""))
                {
                    contentDuringConnect = File.ReadAllText(session.LastCaFile!);
                    if (!OperatingSystem.IsWindows())
                        ownerOnly = File.GetUnixFileMode(session.LastCaFile!) == (UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                return [Ok(line)];
            };

            await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

            var set = LastSetLine(fake);
            Assert.Contains("\"verifyHostCert\",\"true\"", set);
            Assert.Contains($"\"caFile\",{WireArg(session.LastCaFile!)}", set);
            // Not "any": these anchors sign certificates for hosts other than this one, so the engine's own name
            // check is the only thing keeping one of those from verifying here.
            Assert.Contains("\"acceptHostname\",\"\"", set);
            Assert.Equal(FakeTrustAnchorSource.TwoRoots, contentDuringConnect);
            Assert.True(ownerOnly, "the roots file is not owner-only");
            rootsFile = session.LastCaFile!;
            Assert.StartsWith("lizterm-roots-", Path.GetFileName(rootsFile));
            // Kept, unlike a pin file: the bytes are public and identical for every attempt, so a reconnect
            // reuses this file rather than writing a quarter of a megabyte again.
            Assert.True(File.Exists(rootsFile), "the roots file was deleted while the session could still reconnect");

            await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(rootsFile, session.LastCaFile);
        }

        Assert.False(File.Exists(rootsFile), "the roots file outlived the session");
    }

    /// <summary>A plain profile still gets the anchors, and this test is why: b3270 implements the TELNET START-TLS
    /// option, so a connection that began without `L:` can be upgraded to TLS by the host mid-session. Gating the
    /// CA file on Profile.UseTls left that upgrade verifying against the engine's own compiled-in directory — the
    /// nonexistent Homebrew path this milestone exists to stop relying on.</summary>
    [Fact]
    public async Task A_plain_connect_still_gets_the_anchors_because_the_host_may_start_tls()
    {
        var fake = new FakeB3270Process();
        var trust = new FakeTrustAnchorSource();
        await using var session = new B3270Session(Verifying with { UseTls = false }, () => fake) { TrustAnchors = trust };

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains($"\"caFile\",{WireArg(session.LastCaFile!)}", LastSetLine(fake));
        Assert.Equal(FakeTrustAnchorSource.TwoRoots, File.ReadAllText(session.LastCaFile!));
    }

    /// <summary>A pin is a deliberate answer to "trust exactly this"; adding the machine's roots beside it would
    /// widen it back out to every CA the machine trusts.</summary>
    [Fact]
    public async Task A_pin_wins_over_the_trust_anchors()
    {
        var fake = new FakeB3270Process();
        var trust = new FakeTrustAnchorSource();
        string? contentDuringConnect = null;
        await using var session = new B3270Session(Pinned, () => fake) { TrustAnchors = trust };
        fake.RunResponder = line =>
        {
            if (line.Contains("\"Connect\"")) contentDuringConnect = File.ReadAllText(session.LastCaFile!);
            return [Ok(line)];
        };

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Pin.Pem, contentDuringConnect);
        Assert.StartsWith("lizterm-pin-", Path.GetFileName(session.LastCaFile!));
        Assert.Contains("\"acceptHostname\",\"any\"", LastSetLine(fake));
    }

    [Fact]
    public async Task Verification_off_offers_no_trust_anchors()
    {
        var fake = new FakeB3270Process();
        var trust = new FakeTrustAnchorSource();
        await using var session = new B3270Session(Verifying, () => fake) { TrustAnchors = trust };

        await session.ConnectAsync(new ConnectOptions(VerifyCertificate: false), TestContext.Current.CancellationToken);

        Assert.Contains("\"verifyHostCert\",\"false\",\"caFile\",\"\",\"acceptHostname\",\"\"", LastSetLine(fake));
        Assert.Null(session.LastCaFile);
    }

    /// <summary>A machine whose store yields nothing must leave the engine on its own default trust. Writing the
    /// empty PEM instead would make b3270 answer "CA database load ... failed" and never connect at all — worse
    /// than the behaviour this plan set out to fix. An empty or whitespace-only PEM must be treated the same as
    /// null: the source's contract is "null, never empty", but this is the one place a violation of that contract
    /// would actually bite — a `caFile` written from an empty string is a zero-byte file, and b3270 fails every
    /// connect against it just as it would against the fully-empty store this test's null case models.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_source_with_no_anchors_leaves_the_engine_on_its_own_default(string? pem)
    {
        var fake = new FakeB3270Process();
        var trust = new FakeTrustAnchorSource { Pem = pem };
        await using var session = new B3270Session(Verifying, () => fake) { TrustAnchors = trust };

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("\"verifyHostCert\",\"true\",\"caFile\",\"\",\"acceptHostname\",\"\"", LastSetLine(fake));
        Assert.Null(session.LastCaFile);
    }

    /// <summary>Nothing reads the machine's store unless a caller asked for it, so the backend's own tests and any
    /// embedder that has not chosen a source behave the same on every machine.</summary>
    [Fact]
    public async Task The_default_source_offers_nothing()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Verifying, () => fake);

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("\"caFile\",\"\"", LastSetLine(fake));
        Assert.Null(session.LastCaFile);
    }

    private static readonly SessionProfile Reconnecting =
        new() { Name = "t", Host = "h", Port = 23, AutoReconnect = true };

    /// <summary>Only after the Connect run succeeded, which is the whole design: every failure path — the error,
    /// the 30s timeout, the certificate prompt — reasons about an attempt that is over, and none of that holds
    /// with an engine already retrying behind it. The ordering assertion is the point, not the presence one
    /// (spec 6.1).</summary>
    [Fact]
    public async Task Reconnect_is_armed_only_after_the_connect_succeeds()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Reconnecting, () => fake);

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        var lines = fake.InputLines.ToList();
        var connect = lines.FindIndex(l => l.Contains("\"Connect\""));
        var arm = lines.FindIndex(l => l.Contains("\"reconnect\"") && l.Contains("\"true\""));
        Assert.True(connect >= 0, "no Connect was sent");
        Assert.True(arm > connect, "reconnect must be armed after the Connect run, never before it");
    }

    [Fact]
    public async Task Reconnect_is_not_armed_for_a_profile_that_did_not_ask_for_it()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Reconnecting with { AutoReconnect = false }, () => fake);

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(fake.InputLines, l => l.Contains("\"reconnect\""));
    }

    /// <summary>A connect that failed leaves the engine alone: arming there is exactly the `retry` behaviour this
    /// milestone excluded, reached by the back door.</summary>
    [Fact]
    public async Task A_failed_connect_arms_nothing()
    {
        var fake = new FakeB3270Process();
        fake.RunResponder = line => line.Contains("\"Connect\"")
            ? [Failed(Tag(line), "Connection failed"), """{"connection":{"state":"not-connected"}}"""]
            : [Ok(line)];
        await using var session = new B3270Session(Reconnecting, () => fake);

        await Assert.ThrowsAsync<ConnectionFailedException>(
            () => session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.DoesNotContain(fake.InputLines, l => l.Contains("\"reconnect\""));
    }

    /// <summary>Task 12 review, finding 3: every other test on this arm uses the fake's auto-success responder, so
    /// only the accepted path is covered. This refuses the reconnect line specifically -- every other run,
    /// including Connect, still succeeds -- and checks that ConnectAsync completes anyway: throwOnFailure is false
    /// and the arm's own catch swallows what that alone does not cover. Asserting the line was actually sent (not
    /// just that the awaited call did not throw) is what stops this from passing vacuously if the responder match
    /// were ever wrong.</summary>
    [Fact]
    public async Task A_refused_reconnect_arm_still_lets_the_connect_succeed()
    {
        var fake = new FakeB3270Process();
        fake.RunResponder = line => line.Contains("\"reconnect\"")
            ? [Failed(Tag(line), "reconnect not supported")]
            : [Ok(line)];
        await using var session = new B3270Session(Reconnecting, () => fake);

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(fake.InputLines, l => l.Contains("\"reconnect\""));
    }
}
