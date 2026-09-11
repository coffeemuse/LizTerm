// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Diagnostics;
using LizTerm.App.Bell;

namespace LizTerm.App.Tests.Bell;

/// <summary>The one gate in front of both the flash and the sound (bell spec §3.4), driven by an injected clock so
/// nothing here sleeps.</summary>
public class BellThrottleTests
{
    private static readonly TimeSpan Half = TimeSpan.FromMilliseconds(500);
    private static long Ticks(double milliseconds) => (long)(milliseconds / 1000 * Stopwatch.Frequency);

    [Fact]
    public void The_first_bell_is_always_admitted()
    {
        var throttle = new BellThrottle(Half, () => Ticks(0));

        Assert.True(throttle.TryAdmit());
    }

    [Fact]
    public void A_bell_inside_the_interval_is_refused_and_one_at_the_boundary_is_admitted()
    {
        var now = 0.0;
        var throttle = new BellThrottle(Half, () => Ticks(now));
        Assert.True(throttle.TryAdmit());

        now = 499;
        Assert.False(throttle.TryAdmit());

        now = 500;
        Assert.True(throttle.TryAdmit());
    }

    /// <summary>Refused bells do not move the clock: the interval runs from the last bell that rang, so a host
    /// ringing every 100 ms still gets one bell through every 500 ms rather than none at all.</summary>
    [Fact]
    public void A_refused_bell_does_not_restart_the_interval()
    {
        var now = 0.0;
        var throttle = new BellThrottle(Half, () => Ticks(now));
        Assert.True(throttle.TryAdmit());

        for (now = 100; now < 500; now += 100) Assert.False(throttle.TryAdmit());

        now = 500;
        Assert.True(throttle.TryAdmit());
        now = 900;
        Assert.False(throttle.TryAdmit());
        now = 1000;
        Assert.True(throttle.TryAdmit());
    }

    [Fact]
    public void The_default_clock_is_the_stopwatch()
    {
        var throttle = new BellThrottle(Half);

        Assert.True(throttle.TryAdmit());
        Assert.False(throttle.TryAdmit());
    }
}
