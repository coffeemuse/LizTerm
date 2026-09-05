using LizTerm.App.Startup;

namespace LizTerm.App.Tests.Startup;

public class SplashTimingTests
{
    private static readonly DateTime Shown = new(2026, 9, 5, 12, 0, 0);
    private static readonly SplashTiming Timing = SplashTiming.Default;

    [Fact]
    public void Defaults_are_one_and_two_and_a_half_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), Timing.Minimum);
        Assert.Equal(TimeSpan.FromSeconds(2.5), Timing.Maximum);
    }

    [Fact]
    public void Without_a_dismissal_it_closes_at_the_maximum() =>
        Assert.Equal(Shown.AddSeconds(2.5), Timing.CloseAt(Shown, null));

    [Fact]
    public void An_early_dismissal_waits_for_the_minimum() =>
        Assert.Equal(Shown.AddSeconds(1), Timing.CloseAt(Shown, Shown.AddMilliseconds(200)));

    [Fact]
    public void A_late_dismissal_closes_at_once() =>
        Assert.Equal(Shown.AddSeconds(1.7), Timing.CloseAt(Shown, Shown.AddSeconds(1.7)));
}
