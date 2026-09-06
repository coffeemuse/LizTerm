namespace LizTerm.Integration.Tests;

public enum EngineRequirementOutcome
{
    Run,
    Skip,
    Fail,
}

/// <summary>The engine smoke test's gate, kept pure so its three outcomes are asserted without spawning anything:
/// a bundled engine runs the test; none skips it, unless LIZTERM_REQUIRE_ENGINE holds any non-blank value, which
/// turns the skip into a failure so a CI job that just built the engine cannot go green by skipping.</summary>
public static class EngineRequirement
{
    public const string Variable = "LIZTERM_REQUIRE_ENGINE";

    public static EngineRequirementOutcome Decide(bool found, string? requireVariable)
    {
        if (found) return EngineRequirementOutcome.Run;
        return string.IsNullOrWhiteSpace(requireVariable) ? EngineRequirementOutcome.Skip : EngineRequirementOutcome.Fail;
    }
}
