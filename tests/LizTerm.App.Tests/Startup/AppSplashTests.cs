// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Headless.XUnit;
using LizTerm.App.Startup;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Startup;

/// <summary>App's splash wiring, through the internal seam OnFrameworkInitializationCompleted calls — the headless
/// test lifetime never runs that method itself.</summary>
public class AppSplashTests
{
    private static readonly StartupPlan Plan = new StartupPlan.OpenPicker();

    /// <summary>#108: off means no splash at all, not a zero-length one, so the plan runs the moment it is known.
    /// A gate never told there is no splash would wait forever, and under OnExplicitShutdown the process would be
    /// left running with no window.</summary>
    [AvaloniaFact]
    public void With_the_splash_turned_off_none_opens_and_the_plan_runs_as_soon_as_it_is_ready()
    {
        var ran = new List<StartupPlan>();
        var gate = new StartupGate(ran.Add);

        var splash = App.OpenSplash(gate, new AppSettings(ShowSplashOnLaunch: false));
        gate.PlanReady(Plan);

        Assert.Null(splash);
        Assert.Equal([Plan], ran);
    }

    /// <summary>The default: nothing opens under the splash, and its close is what lets the plan run.</summary>
    [AvaloniaFact]
    public void With_the_splash_on_the_plan_waits_for_the_splash_to_close()
    {
        var ran = new List<StartupPlan>();
        var gate = new StartupGate(ran.Add);

        var splash = App.OpenSplash(gate, new AppSettings());
        gate.PlanReady(Plan);

        Assert.NotNull(splash);
        Assert.True(splash.IsVisible);
        Assert.Empty(ran);

        splash.Close();

        Assert.Equal([Plan], ran);
    }
}
