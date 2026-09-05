namespace LizTerm.App.Startup;

/// <summary>Runs the startup plan exactly once, when both the plan is known and the splash has closed, in
/// whichever order those happen. A splash whose maximum has already elapsed closes synchronously inside
/// <c>Show()</c>, so <c>App</c> cannot rely on subscribing to <c>Closed</c> before the window can raise it; with
/// <see cref="Avalonia.Controls.ShutdownMode.OnExplicitShutdown"/> a missed plan would leave the process running
/// with no window and no way to quit. Called only on the UI thread, so no locking.</summary>
public sealed class StartupGate(Action<StartupPlan> execute)
{
    private StartupPlan? _plan;
    private bool _splashClosed;
    private bool _executed;

    public void PlanReady(StartupPlan plan)
    {
        _plan ??= plan;
        RunWhenReady();
    }

    public void SplashClosed()
    {
        _splashClosed = true;
        RunWhenReady();
    }

    private void RunWhenReady()
    {
        if (_executed || !_splashClosed || _plan is not { } plan) return;
        _executed = true;
        execute(plan);
    }
}
