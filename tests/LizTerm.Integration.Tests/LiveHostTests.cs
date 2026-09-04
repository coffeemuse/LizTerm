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

        var colon = target!.LastIndexOf(':');
        var host = colon > 0 ? target[..colon] : target;
        var port = colon > 0 ? int.Parse(target[(colon + 1)..]) : 23;
        var profile = new SessionProfile
        {
            Name = "integration",
            Host = host,
            Port = port,
            UseTls = Flag("LIZTERM_TEST_TLS", fallback: false),
            VerifyCertificate = Flag("LIZTERM_TEST_VERIFY_CERT", fallback: true),
        };

        await using var session = new B3270Session(profile, () => new B3270ChildProcess(B3270Locator.Find()), WireLog.FromEnvironment());
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

    private static bool Flag(string variable, bool fallback) =>
        Environment.GetEnvironmentVariable(variable)?.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" => true,
            "0" or "false" or "no" => false,
            _ => fallback,
        };
}
