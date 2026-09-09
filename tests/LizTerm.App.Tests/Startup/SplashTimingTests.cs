// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Startup;

namespace LizTerm.App.Tests.Startup;

public class SplashTimingTests
{
    private static readonly SplashTiming Timing = SplashTiming.Default;

    [Fact]
    public void Defaults_are_one_and_two_and_a_half_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), Timing.Minimum);
        Assert.Equal(TimeSpan.FromSeconds(2.5), Timing.Maximum);
    }

    [Fact]
    public void Without_a_dismissal_it_closes_at_the_maximum() =>
        Assert.Equal(TimeSpan.FromSeconds(2.5), Timing.CloseAfter(TimeSpan.Zero, null));

    [Fact]
    public void An_early_dismissal_waits_for_the_minimum() =>
        Assert.Equal(TimeSpan.FromMilliseconds(800), Timing.CloseAfter(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200)));

    [Fact]
    public void A_late_dismissal_closes_at_once() =>
        Assert.Equal(TimeSpan.Zero, Timing.CloseAfter(TimeSpan.FromSeconds(1.7), TimeSpan.FromSeconds(1.7)));

    /// <summary>Elapsed time is monotonic, so the only way past the maximum is a slow start; close at once.</summary>
    [Fact]
    public void A_splash_already_past_the_maximum_closes_at_once() =>
        Assert.True(Timing.CloseAfter(TimeSpan.FromSeconds(3), null) <= TimeSpan.Zero);
}
