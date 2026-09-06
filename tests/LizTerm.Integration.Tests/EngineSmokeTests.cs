using LizTerm.Backend.B3270;
using LizTerm.Backend.B3270.Process;
using LizTerm.Core.Session;

namespace LizTerm.Integration.Tests;

/// <summary>Proves the engine bundled in this project's output (runtimes/&lt;rid&gt;/native/b3270, copied from native/out
/// by the csproj at build time) can be spawned and spoken to. Never connects to anything. Resolves the bundled engine
/// only: LIZTERM_B3270_PATH is ignored, so a Homebrew b3270 cannot stand in for the one CI built. Without a bundled
/// engine it skips, unless LIZTERM_REQUIRE_ENGINE is set, in which case it fails; a binary that is present but did
/// not resolve (the not-executable arm) always fails, whatever the variable says (see EngineRequirement).</summary>
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

        // Find throws the same exception type for "nothing anywhere" and "there but not executable", so ask the
        // locator where it looked: a candidate that exists on disk means the second, which must never skip.
        var present = B3270Locator.Candidates(overridePath: null, AppContext.BaseDirectory).Any(c => File.Exists(c.Path));
        var outcome = EngineRequirement.Decide(location is not null, present, Environment.GetEnvironmentVariable(EngineRequirement.Variable));
        if (outcome == EngineRequirementOutcome.Fail) Assert.Fail(missing ?? "no bundled engine");
        Assert.SkipWhen(outcome == EngineRequirementOutcome.Skip, missing ?? "no bundled engine");

        // The point of the macOS job: the csproj copy rule put the freshly built binary in runtimes/<rid>/native/.
        // Find's other bundled candidate is a b3270 sitting beside the test assembly, which would satisfy a check
        // on Engine.Source while the copy rule was broken.
        Assert.Contains(B3270Locator.BundledDirectory, location!.Path);

        var profile = new SessionProfile(Name: "engine-smoke", Host: "engine-smoke.invalid");
        await using var session = new B3270Session(profile, () => new B3270ChildProcess(location.Path), location: location);
        await session.StartProcessAsync(TestContext.Current.CancellationToken);

        // StartProcessAsync itself rejects an unparseable or too-old version, so reaching here with a version at
        // all is the assertion; re-parsing Engine.Version would only couple this test to its display format.
        Assert.NotNull(session.Engine.Version);
    }
}
