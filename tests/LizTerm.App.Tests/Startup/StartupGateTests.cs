using LizTerm.App.Startup;

namespace LizTerm.App.Tests.Startup;

public class StartupGateTests
{
    private static readonly StartupPlan Plan = new StartupPlan.OpenPicker();

    [Fact]
    public void The_plan_runs_once_the_splash_closes()
    {
        var ran = new List<StartupPlan>();
        var gate = new StartupGate(ran.Add);
        gate.PlanReady(Plan);
        Assert.Empty(ran);
        gate.SplashClosed();
        Assert.Equal([Plan], ran);
    }

    /// <summary>A splash whose maximum has already elapsed closes inside Show(), before App can subscribe and
    /// before the plan is known. The plan must still run when it arrives, or the process is left with no window.</summary>
    [Fact]
    public void The_plan_runs_even_when_the_splash_closed_first()
    {
        var ran = new List<StartupPlan>();
        var gate = new StartupGate(ran.Add);
        gate.SplashClosed();
        Assert.Empty(ran);
        gate.PlanReady(Plan);
        Assert.Equal([Plan], ran);
    }

    [Fact]
    public void The_plan_runs_only_once()
    {
        var ran = new List<StartupPlan>();
        var gate = new StartupGate(ran.Add);
        gate.SplashClosed();
        gate.PlanReady(Plan);
        gate.SplashClosed();
        gate.PlanReady(new StartupPlan.ShowError("second"));
        Assert.Equal([Plan], ran);
    }
}
