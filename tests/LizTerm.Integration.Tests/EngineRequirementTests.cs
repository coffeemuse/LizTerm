namespace LizTerm.Integration.Tests;

public class EngineRequirementTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    public void A_found_engine_runs_whatever_the_variable_says(string? variable) =>
        Assert.Equal(EngineRequirementOutcome.Run, EngineRequirement.Decide(found: true, present: true, variable));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_engine_skips_while_the_variable_is_unset_or_blank(string? variable) =>
        Assert.Equal(EngineRequirementOutcome.Skip, EngineRequirement.Decide(found: false, present: false, variable));

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("0")]
    public void A_missing_engine_fails_once_the_variable_holds_any_non_blank_value(string variable) =>
        Assert.Equal(EngineRequirementOutcome.Fail, EngineRequirement.Decide(found: false, present: false, variable));

    /// <summary>A binary that is there but did not resolve is a broken engine, not an absent one: it fails even
    /// with the variable unset, so a downloaded artifact missing its executable bit cannot skip quietly.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]
    public void A_present_but_unresolved_engine_always_fails(string? variable) =>
        Assert.Equal(EngineRequirementOutcome.Fail, EngineRequirement.Decide(found: false, present: true, variable));
}
