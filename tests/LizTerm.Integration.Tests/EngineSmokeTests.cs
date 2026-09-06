using LizTerm.Backend.B3270;
using LizTerm.Backend.B3270.Process;
using LizTerm.Core.Session;

namespace LizTerm.Integration.Tests;

/// <summary>Proves the engine bundled in this project's output (runtimes/&lt;rid&gt;/native/b3270, copied from native/out
/// by the csproj at build time) can be spawned and spoken to. Never connects to anything. Resolves the bundled engine
/// only: LIZTERM_B3270_PATH is ignored, so a Homebrew b3270 cannot stand in for the one CI built. Without a bundled
/// engine it skips, unless LIZTERM_REQUIRE_ENGINE is set, in which case it fails (see EngineRequirement).</summary>
public class EngineSmokeTests
{
    [Fact(Timeout = 60_000)]
    public async Task Bundled_engine_starts_and_reports_its_version()
    {
        B3270Location? location = null;
        string? missing = null;
        try
        {
            location = B3270Locator.Find(overridePath: null, AppContext.BaseDirectory);
        }
        catch (BackendUnavailableException e)
        {
            missing = e.Message;
        }

        var outcome = EngineRequirement.Decide(location is not null, Environment.GetEnvironmentVariable(EngineRequirement.Variable));
        if (outcome == EngineRequirementOutcome.Fail) Assert.Fail($"{EngineRequirement.Variable} is set but {missing}");
        Assert.SkipWhen(outcome == EngineRequirementOutcome.Skip, missing ?? "no bundled engine");

        var profile = new SessionProfile(Name: "engine-smoke", Host: "engine-smoke.invalid");
        await using var session = new B3270Session(profile, () => new B3270ChildProcess(location!.Path), location: location);
        await session.StartProcessAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EngineSource.Bundled, session.Engine.Source);
        var reported = session.Engine.Version;
        Assert.NotNull(reported);
        var number = Version.Parse(reported.Split(' ', 2)[0]);
        Assert.True(number >= B3270Session.MinimumVersion, $"engine {reported} is older than {B3270Session.MinimumVersion}");
    }
}
