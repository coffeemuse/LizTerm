using LizTerm.App.Status;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests;

public class SessionFactoryTests
{
    /// <summary>With the override pointed at a file that does not exist and the factory pointed at an empty
    /// directory instead of the app's own base directory, the factory must still hand back a session whose first
    /// connect reports the locator's explanation.</summary>
    [Fact]
    public async Task Missing_engine_is_reported_by_the_first_connect_not_by_Create()
    {
        var bogus = Path.Combine(Path.GetTempPath(), "lizterm-missing-" + Guid.NewGuid().ToString("N"), "b3270");
        var emptyDirectory = Directory.CreateTempSubdirectory("lizterm-empty-");
        try
        {
            await using var session = SessionFactory.Create(new SessionProfile { Name = "t", Host = "h" }, bogus, emptyDirectory.FullName);
            var ex = await Assert.ThrowsAsync<BackendUnavailableException>(() => session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Contains("Looked in", ex.Message);
            Assert.Contains(bogus, ex.Message);
        }
        finally
        {
            emptyDirectory.Delete(recursive: true);
        }
    }

    /// <summary>Regression: the not-found sentinel carried EngineSource.Bundled, so Help > About told a user
    /// whose override had broken that the engine ships inside the app, with a blank path underneath.</summary>
    [Fact]
    public async Task A_missing_engine_is_not_reported_as_bundled()
    {
        var bogus = Path.Combine(Path.GetTempPath(), "lizterm-missing-" + Guid.NewGuid().ToString("N"), "b3270");
        var emptyDirectory = Directory.CreateTempSubdirectory("lizterm-empty-");
        try
        {
            await using var session = SessionFactory.Create(new SessionProfile { Name = "t", Host = "h" }, bogus, emptyDirectory.FullName);
            Assert.Equal(EngineSource.Unknown, session.Engine.Source);
            Assert.Equal("b3270, not found", StatusFormatter.Engine(session.Engine, SessionFactory.OverrideOrigin));
        }
        finally
        {
            emptyDirectory.Delete(recursive: true);
        }
    }

    /// <summary>The two tests above drive the seam, so nothing else would notice the public overload drifting off
    /// the default the app actually ships with — passing Environment.CurrentDirectory, or reading the wrong
    /// variable. Both public entry points resolve through DefaultLocation; this pins that they agree.</summary>
    [Fact]
    public async Task The_public_Create_resolves_through_the_same_default_as_CheckBackend()
    {
        var (overridePath, baseDirectory) = SessionFactory.DefaultLocation;
        Assert.Equal(Environment.GetEnvironmentVariable(SessionFactory.OverrideOrigin), overridePath);
        Assert.Equal(AppContext.BaseDirectory, baseDirectory);

        var profile = new SessionProfile { Name = "t", Host = "h" };
        await using var viaDefault = SessionFactory.Create(profile);
        await using var viaSeam = SessionFactory.Create(profile, overridePath, baseDirectory);
        Assert.Equal(viaSeam.Engine, viaDefault.Engine);
    }
}
