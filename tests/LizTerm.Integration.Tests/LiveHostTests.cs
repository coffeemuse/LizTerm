using LizTerm.Backend.B3270;
using LizTerm.Backend.B3270.Process;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.Integration.Tests;

/// <summary>Runs only when LIZTERM_TEST_HOST=host[:port] is set. Uses the bundled b3270 when this
/// project was built after native/build/build-macos.sh; otherwise set LIZTERM_B3270_PATH.</summary>
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
        var profile = new SessionProfile { Name = "integration", Host = host, Port = port };

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
    }
}
