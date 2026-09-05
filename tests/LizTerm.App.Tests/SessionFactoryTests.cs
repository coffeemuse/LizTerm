using LizTerm.App.Status;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests;

public class SessionFactoryTests
{
    /// <summary>With the override pointed at a file that does not exist and no bundled engine in this test's output,
    /// the factory must still hand back a session whose first connect reports the locator's explanation.</summary>
    [Fact]
    public async Task Missing_engine_is_reported_by_the_first_connect_not_by_Create()
    {
        var original = Environment.GetEnvironmentVariable("LIZTERM_B3270_PATH");
        var bogus = Path.Combine(Path.GetTempPath(), "lizterm-missing-" + Guid.NewGuid().ToString("N"), "b3270");
        try
        {
            Environment.SetEnvironmentVariable("LIZTERM_B3270_PATH", bogus);
            await using var session = SessionFactory.Create(new SessionProfile { Name = "t", Host = "h" });
            var ex = await Assert.ThrowsAsync<BackendUnavailableException>(() => session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Contains("Looked in", ex.Message);
            Assert.Contains(bogus, ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LIZTERM_B3270_PATH", original);
        }
    }

    /// <summary>Regression: the not-found sentinel carried EngineSource.Bundled, so Help > About told a user
    /// whose override had broken that the engine ships inside the app, with a blank path underneath.</summary>
    [Fact]
    public async Task A_missing_engine_is_not_reported_as_bundled()
    {
        var original = Environment.GetEnvironmentVariable("LIZTERM_B3270_PATH");
        var bogus = Path.Combine(Path.GetTempPath(), "lizterm-missing-" + Guid.NewGuid().ToString("N"), "b3270");
        try
        {
            Environment.SetEnvironmentVariable("LIZTERM_B3270_PATH", bogus);
            await using var session = SessionFactory.Create(new SessionProfile { Name = "t", Host = "h" });
            Assert.Equal(EngineSource.Unknown, session.Engine.Source);
            Assert.Equal("b3270, not found", StatusFormatter.Engine(session.Engine, SessionFactory.OverrideOrigin));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LIZTERM_B3270_PATH", original);
        }
    }
}
