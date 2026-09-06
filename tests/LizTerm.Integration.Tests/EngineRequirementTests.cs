namespace LizTerm.Integration.Tests;

public class EngineRequirementTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    public void A_found_engine_runs_whatever_the_variable_says(string? variable) =>
        Assert.Equal(EngineRequirementOutcome.Run, EngineRequirement.Decide(found: true, variable));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_engine_skips_while_the_variable_is_unset_or_blank(string? variable) =>
        Assert.Equal(EngineRequirementOutcome.Skip, EngineRequirement.Decide(found: false, variable));

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("0")]
    public void A_missing_engine_fails_once_the_variable_holds_any_non_blank_value(string variable) =>
        Assert.Equal(EngineRequirementOutcome.Fail, EngineRequirement.Decide(found: false, variable));
}
