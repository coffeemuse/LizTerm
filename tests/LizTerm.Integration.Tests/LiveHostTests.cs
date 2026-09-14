// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LizTerm.Backend.B3270;
using LizTerm.Backend.B3270.Process;
using LizTerm.Core.Screen;
using LizTerm.Core.Security;
using LizTerm.Core.Session;

namespace LizTerm.Integration.Tests;

/// <summary>Runs only when LIZTERM_TEST_HOST=host[:port] is set. LIZTERM_TEST_TLS=1 connects over TLS and
/// LIZTERM_TEST_VERIFY_CERT=0 accepts an unverifiable certificate; both default the way a profile does.
/// Uses the bundled b3270 when this project was built after native/build/build-macos.sh; otherwise set
/// LIZTERM_B3270_PATH.</summary>
public class LiveHostTests
{
    private const int LiveTimeout = 600_000;

    [Fact(Timeout = LiveTimeout)]
    public async Task Connects_and_receives_a_screen()
    {
        var target = Environment.GetEnvironmentVariable("LIZTERM_TEST_HOST");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(target), "LIZTERM_TEST_HOST is not set");

        var profile = ProfileFor(target!);

        await using var session = new B3270Session(profile, () => new B3270ChildProcess(B3270Locator.Find().Path), WireLog.TryFromEnvironment(out _));
        var gotText = new TaskCompletionSource<ScreenSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.ScreenUpdated += (_, s) =>
        {
            if (s.ToText().Any(char.IsLetterOrDigit)) gotText.TrySetResult(s);
        };

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        var screen = await gotText.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        Assert.True(session.ConnectionState.IsConnected(), $"state was {session.ConnectionState}");
        Assert.True(screen.ToText().Any(char.IsLetter), "screen has no letters");
        if (profile.UseTls) Assert.True(session.Tls?.Secure, "session is not secure");
    }

    /// <summary>Needs a TLS host whose certificate cannot be verified (LIZTERM_TEST_TLS=1, LIZTERM_TEST_VERIFY_CERT=0);
    /// connects with verification forced on and expects the flagged failure.</summary>
    [Fact(Timeout = LiveTimeout)]
    public async Task Verify_on_connect_to_a_self_signed_host_is_flagged()
    {
        var target = Environment.GetEnvironmentVariable("LIZTERM_TEST_HOST");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(target), "LIZTERM_TEST_HOST is not set");
        var profile = ProfileFor(target!);
        Assert.SkipUnless(profile is { UseTls: true, VerifyCertificate: false }, "needs LIZTERM_TEST_TLS=1 and LIZTERM_TEST_VERIFY_CERT=0");
        await using var session = new B3270Session(profile, () => new B3270ChildProcess(B3270Locator.Find().Path), WireLog.TryFromEnvironment(out _));
        var ex = await Assert.ThrowsAsync<ConnectionFailedException>(() =>
            session.ConnectAsync(new ConnectOptions(VerifyCertificate: true), TestContext.Current.CancellationToken));
        Assert.True(ex.CertificateVerificationFailed, string.Join(" | ", ex.Lines));
        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
    }

    /// <summary>Spec 9: a pin made from what the gateway presents verifies (the tls indication reports verified),
    /// and a decoy pin fails with the certificate flag on the same session. The profile from the environment has
    /// verification off, so both attempts force it on; a pin is ignored when verification is off (spec 3.1). Needs
    /// the TLS gateway (LIZTERM_TEST_TLS=1, LIZTERM_TEST_VERIFY_CERT=0). Run alone with LIZTERM_WIRE_LOG set to
    /// record the gateway-pinned-login fixture.</summary>
    [Fact(Timeout = LiveTimeout)]
    public async Task A_pinned_certificate_verifies_and_a_decoy_pin_fails()
    {
        var target = Environment.GetEnvironmentVariable("LIZTERM_TEST_HOST");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(target), "LIZTERM_TEST_HOST is not set");
        var profile = ProfileFor(target!);
        Assert.SkipUnless(profile is { UseTls: true, VerifyCertificate: false }, "needs LIZTERM_TEST_TLS=1 and LIZTERM_TEST_VERIFY_CERT=0");
        var ct = TestContext.Current.CancellationToken;

        var presented = await new SslStreamCertificateFetcher().FetchAsync(profile.Host, profile.Port, ct);
        Assert.True(presented.Pinnable, presented.NotPinnableReason);
        var pin = new CertificatePin(presented.Sha256, presented.Subject, presented.Pem);

        await using var session = new B3270Session(profile, () => new B3270ChildProcess(B3270Locator.Find().Path), WireLog.TryFromEnvironment(out _));
        await session.ConnectAsync(new ConnectOptions(VerifyCertificate: true, Pin: pin), ct);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (session.Tls?.Verified != true && DateTime.UtcNow < deadline) await Task.Delay(50, ct);
        Assert.True(session.Tls?.Secure, "session is not secure");
        Assert.True(session.Tls?.Verified, "the pinned connect was not verified");
        await session.DisconnectAsync();

        using var decoyKey = RSA.Create(2048);
        using var decoy = new CertificateRequest("CN=localhost", decoyKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var decoyPin = new CertificatePin(CertificateReader.Fingerprint(decoy), decoy.Subject, decoy.ExportCertificatePem() + "\n");
        var ex = await Assert.ThrowsAsync<ConnectionFailedException>(() =>
            session.ConnectAsync(new ConnectOptions(VerifyCertificate: true, Pin: decoyPin), ct));
        Assert.True(ex.CertificateVerificationFailed, string.Join(" | ", ex.Lines));
        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
    }

    /// <summary>A plain connect to a TLS listener never completes; the token must end it and leave the session usable.</summary>
    [Fact(Timeout = LiveTimeout)]
    public async Task Plain_connect_to_a_tls_port_is_cancelled_by_the_token_and_the_session_recovers()
    {
        var target = Environment.GetEnvironmentVariable("LIZTERM_TEST_HOST");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(target), "LIZTERM_TEST_HOST is not set");
        var profile = ProfileFor(target!);
        Assert.SkipUnless(profile.UseTls, "needs LIZTERM_TEST_TLS=1");
        await using var session = new B3270Session(profile with { UseTls = false }, () => new B3270ChildProcess(B3270Locator.Find().Path), WireLog.TryFromEnvironment(out _));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ConnectAsync(cancellationToken: cts.Token));
            Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
        }
        Assert.NotNull(session.Engine.Version);
    }

    private static SessionProfile ProfileFor(string target)
    {
        var colon = target.LastIndexOf(':');
        var host = colon > 0 ? target[..colon] : target;
        var port = colon > 0 ? int.Parse(target[(colon + 1)..]) : 23;
        return new SessionProfile
        {
            Name = "integration",
            Host = host,
            Port = port,
            UseTls = Flag("LIZTERM_TEST_TLS", fallback: false),
            VerifyCertificate = Flag("LIZTERM_TEST_VERIFY_CERT", fallback: true),
        };
    }

    private sealed class ProgressLog : IProgress<long>
    {
        private readonly List<long> _values = [];
        public long[] Values { get { lock (_values) return _values.ToArray(); } }
        public void Report(long value) { lock (_values) _values.Add(value); }
    }

    /// <summary>Sends a small text file to LIZTERM.ITEST under the TSO user's prefix, receives it back, and compares
    /// with trailing blanks trimmed per line (IND$FILE pads records to the record length). Needs the three
    /// LIZTERM_TEST_* variables; the credentials are typed through TypeTextAsync, so a wire log of this run
    /// contains the password on its outbound side and must never be committed.</summary>
    [Fact(Timeout = LiveTimeout)]
    public async Task Indfile_round_trip_matches()
    {
        var target = Environment.GetEnvironmentVariable("LIZTERM_TEST_HOST");
        var user = Environment.GetEnvironmentVariable("LIZTERM_TEST_USER");
        var password = Environment.GetEnvironmentVariable("LIZTERM_TEST_PASSWORD");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(target), "LIZTERM_TEST_HOST is not set");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(user), "LIZTERM_TEST_USER is not set");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(password), "LIZTERM_TEST_PASSWORD is not set");
        var ct = TestContext.Current.CancellationToken;

        var dir = Directory.CreateTempSubdirectory("lizterm-indfile-");
        var sent = Path.Combine(dir.FullName, "sent.txt");
        var received = Path.Combine(dir.FullName, "received.txt");
        await File.WriteAllTextAsync(sent, "LizTerm IND$FILE round trip\nsecond line with lowercase text\nthird line has trailing spaces   \n//JOB1 JOB (ACCT),'LIZTERM',CLASS=A\nEND\n", ct);

        await using var session = new B3270Session(ProfileFor(target!), () => new B3270ChildProcess(B3270Locator.Find().Path), WireLog.TryFromEnvironment(out _));
        using var screens = new ScreenWaiter(session);
        var tso = new TsoNavigator(session, screens);
        var dataset = "LIZTERM.ITEST";

        await session.ConnectAsync(cancellationToken: ct);
        try
        {
            // Inside the try: the password is accepted partway through LogonAsync, so a failure in the rest of it
            // still has to reach the LOGOFF below or the userid stays logged on and the next run is refused.
            await tso.LogonAsync(user!.Trim(), password!);

            var progress = new ProgressLog();
            var up = await session.TransferAsync(new FileTransferRequest { Direction = TransferDirection.Send, LocalPath = sent, HostFile = dataset }, progress, ct);
            Assert.True(up.Succeeded, "send failed: " + up.Message);
            await tso.ReachReadyAsync();

            var down = await session.TransferAsync(new FileTransferRequest { Direction = TransferDirection.Receive, LocalPath = received, HostFile = dataset }, progress, ct);
            Assert.True(down.Succeeded, "receive failed: " + down.Message);
            await tso.ReachReadyAsync();

            Assert.True(progress.Values.Any(v => v > 0), "no bytes were reported for the transfers");
            var expected = (await File.ReadAllLinesAsync(sent, ct)).Select(l => l.TrimEnd());
            var actual = (await File.ReadAllLinesAsync(received, ct)).Select(l => l.TrimEnd());
            Assert.Equal(expected, actual);
        }
        finally
        {
            // One try/catch per step, in this order: DELETE is the one that needs READY and so the one most likely
            // to throw after a failure, and it must not take LOGOFF down with it. A leftover dataset is harmless
            // (the next PUT replaces it); a userid left logged on blocks the next run.
            await CleanupStepAsync("DELETE", () => tso.CommandAsync($"DELETE '{user!.Trim()}.{dataset}'"));
            await CleanupStepAsync("LOGOFF", () => tso.LogoffAsync());
            await CleanupStepAsync("disconnect", () => session.DisconnectAsync());
            await CleanupStepAsync("delete scratch dir", () => { dir.Delete(recursive: true); return Task.CompletedTask; });
        }
    }

    /// <summary>The same round trip started from ISPF's primary menu with the ISPF (MVS) host type, so the engine types
    /// TSO ahead of IND$FILE. Only the bundled engine carries LizTerm's CommandPrefix patch, so this test never uses
    /// LIZTERM_B3270_PATH. Skips when TSO answers ISPF with "not found". Run alone with LIZTERM_WIRE_LOG set, its log is
    /// the source of Fixtures/indfile-ispf-roundtrip.jsonl; the log holds the password on its outbound side and must
    /// never be committed.</summary>
    [Fact(Timeout = LiveTimeout)]
    public async Task Indfile_round_trip_from_ispf_matches()
    {
        var target = Environment.GetEnvironmentVariable("LIZTERM_TEST_HOST");
        var user = Environment.GetEnvironmentVariable("LIZTERM_TEST_USER");
        var password = Environment.GetEnvironmentVariable("LIZTERM_TEST_PASSWORD");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(target), "LIZTERM_TEST_HOST is not set");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(user), "LIZTERM_TEST_USER is not set");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(password), "LIZTERM_TEST_PASSWORD is not set");
        var engine = BundledEngine.Require();
        var ct = TestContext.Current.CancellationToken;

        var dir = Directory.CreateTempSubdirectory("lizterm-ispf-");
        var sent = Path.Combine(dir.FullName, "sent.txt");
        var received = Path.Combine(dir.FullName, "received.txt");
        await File.WriteAllTextAsync(sent, "LizTerm IND$FILE round trip from ISPF\nsecond line with lowercase text\nthird line has trailing spaces   \nEND\n", ct);

        await using var session = new B3270Session(ProfileFor(target!), () => new B3270ChildProcess(engine.Path), WireLog.TryFromEnvironment(out _), location: engine);
        using var screens = new ScreenWaiter(session);
        var tso = new TsoNavigator(session, screens);
        var dataset = "LIZTERM.ISPFTEST";

        await session.ConnectAsync(cancellationToken: ct);
        try
        {
            await tso.LogonAsync(user!.Trim(), password!);
            Assert.SkipUnless(await tso.StartIspfAsync(), "TSO answered ISPF with \"not found\": this host has no ISPF");

            var progress = new ProgressLog();
            var up = await session.TransferAsync(new FileTransferRequest { Direction = TransferDirection.Send, LocalPath = sent, HostFile = dataset, HostType = TransferHostType.Ispf }, progress, ct);
            Assert.True(up.Succeeded, "send failed: " + up.Message);
            await tso.WaitForIspfPanelAsync();

            var down = await session.TransferAsync(new FileTransferRequest { Direction = TransferDirection.Receive, LocalPath = received, HostFile = dataset, HostType = TransferHostType.Ispf }, progress, ct);
            Assert.True(down.Succeeded, "receive failed: " + down.Message);
            await tso.WaitForIspfPanelAsync();

            Assert.True(progress.Values.Any(v => v > 0), "no bytes were reported for the transfers");
            var expected = (await File.ReadAllLinesAsync(sent, ct)).Select(l => l.TrimEnd());
            var actual = (await File.ReadAllLinesAsync(received, ct)).Select(l => l.TrimEnd());
            Assert.Equal(expected, actual);

            // Leaving ISPF here, not only in the finally, so a broken leftover-panel rule fails the test instead of
            // becoming a hidden cleanup diagnostic.
            await tso.ReachReadyAsync();
        }
        finally
        {
            // A fallback for a run that failed inside ISPF: DELETE needs READY. PF3 ends Wally ISPF and ReachReadyAsync
            // clears the panel it leaves behind. A session already at READY returns at once.
            await CleanupStepAsync("leave ISPF", () => tso.ReachReadyAsync());
            await CleanupStepAsync("DELETE", () => tso.CommandAsync($"DELETE '{user!.Trim()}.{dataset}'"));
            await CleanupStepAsync("LOGOFF", () => tso.LogoffAsync());
            await CleanupStepAsync("disconnect", () => session.DisconnectAsync());
            await CleanupStepAsync("delete scratch dir", () => { dir.Delete(recursive: true); return Task.CompletedTask; });
        }
    }

    /// <summary>Runs one cleanup step, reporting a failure as a diagnostic rather than throwing, so the steps after
    /// it still run.</summary>
    private static async Task CleanupStepAsync(string step, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            TestContext.Current.SendDiagnosticMessage($"cleanup step {step} failed: {ex.Message}");
        }
    }

    private static bool Flag(string variable, bool fallback) =>
        Environment.GetEnvironmentVariable(variable)?.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" => true,
            "0" or "false" or "no" => false,
            _ => fallback,
        };
}
