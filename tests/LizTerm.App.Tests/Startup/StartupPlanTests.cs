using LizTerm.App.Startup;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Startup;

public class StartupPlanTests
{
    private static readonly SessionProfile[] Profiles = [new() { Name = "TK5", Host = "mvs.local" }];

    [Fact]
    public void Backend_error_wins_over_everything()
    {
        var plan = StartupPlan.Decide("b3270 missing", StartupArguments.Parse(["TK5"]), Profiles);
        Assert.Equal(new StartupPlan.ShowError("b3270 missing"), plan);
    }

    [Fact]
    public void A_resolved_argument_opens_a_session()
    {
        var plan = Assert.IsType<StartupPlan.OpenSession>(StartupPlan.Decide(null, StartupArguments.Parse(["tk5"]), Profiles));
        Assert.Same(Profiles[0], plan.Profile);
        Assert.True(plan.FromStore);

        var adHoc = Assert.IsType<StartupPlan.OpenSession>(StartupPlan.Decide(null, StartupArguments.Parse(["L:mvs.local"]), Profiles));
        Assert.Equal("mvs.local:992", adHoc.Profile.Name);
        Assert.False(adHoc.FromStore);
    }

    [Theory]
    [InlineData("")]
    [InlineData("missing")]
    [InlineData("X:bad")]
    public void Otherwise_the_picker_opens(string arg)
    {
        var args = arg.Length == 0 ? Array.Empty<string>() : [arg];
        Assert.Equal(new StartupPlan.OpenPicker(), StartupPlan.Decide(null, StartupArguments.Parse(args), Profiles));
    }
}
