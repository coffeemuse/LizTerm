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
        await fake.WaitForInputAsync(l => l.Contains("\"Connect\""), TimeSpan.FromSeconds(1));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt);
        Assert.Contains(fake.InputLines, l => l.Contains("\"Disconnect\""));
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
}
