using LizTerm.Backend.B3270;
using LizTerm.Backend.B3270.Process;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.Integration.Tests;

/// <summary>Runs only when LIZTERM_TEST_HOST=host[:port] is set. LIZTERM_TEST_TLS=1 connects over TLS and
/// LIZTERM_TEST_VERIFY_CERT=0 accepts an unverifiable certificate; both default the way a profile does.
/// Uses the bundled b3270 when this project was built after native/build/build-macos.sh; otherwise set
/// LIZTERM_B3270_PATH.</summary>
public class LiveHostTests
{
    [Fact]
    public async Task Connects_and_receives_a_screen()
    {
        var target = Environment.GetEnvironmentVariable("LIZTERM_TEST_HOST");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(target), "LIZTERM_TEST_HOST is not set");

        var profile = ProfileFor(target!);

        await using var session = new B3270Session(profile, () => new B3270ChildProcess(B3270Locator.Find().Path), WireLog.FromEnvironment());
        var gotText = new TaskCompletionSource<ScreenSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.ScreenUpdated += (_, s) =>
        {
            if (s.ToText().Any(char.IsLetterOrDigit)) gotText.TrySetResult(s);
        };

        await session.ConnectAsync(TestContext.Current.CancellationToken);
        var screen = await gotText.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        Assert.True(session.ConnectionState.IsConnected(), $"state was {session.ConnectionState}");
        Assert.True(screen.ToText().Any(char.IsLetter), "screen has no letters");
        if (profile.UseTls) Assert.True(session.Tls?.Secure, "session is not secure");
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
    [Fact]
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

        await using var session = new B3270Session(ProfileFor(target!), () => new B3270ChildProcess(B3270Locator.Find().Path), WireLog.FromEnvironment());
        using var screens = new ScreenWaiter(session);
        var tso = new TsoNavigator(session, screens);
        var dataset = "LIZTERM.ITEST";

        await session.ConnectAsync(ct);
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
