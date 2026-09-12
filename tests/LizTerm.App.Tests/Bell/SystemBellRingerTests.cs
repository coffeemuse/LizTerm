// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Bell;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Bell;

/// <summary>There is no way to assert a sound, which is the point of the IBellRinger seam. What can be asserted is
/// that the platform call is reachable and does not throw here: on macOS that is NSBeep, on Windows MessageBeep,
/// on Linux the deliberate no-op. On macOS and Windows the call is audible, so it runs only when LIZTERM_TEST_BELL
/// is set, the way the live-host tests gate themselves; on Linux it is silent and always runs. Record.Exception
/// makes that assertion explicit in the code rather than leaning on xUnit's implicit "no exception thrown = pass".</summary>
public class SystemBellRingerTests
{
    private static bool Audible => OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();

    [Fact]
    public void CanRing_says_yes_to_the_system_alert_where_the_platform_has_one_and_never_to_None()
    {
        var ringer = new SystemBellRinger();

        Assert.Equal(Audible, ringer.CanRing(BellSound.SystemAlert));
        Assert.False(ringer.CanRing(BellSound.None));
    }

    [Fact]
    public void Ring_does_not_throw_on_this_platform()
    {
        Assert.SkipWhen(Audible && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LIZTERM_TEST_BELL")),
            "LIZTERM_TEST_BELL is not set, and the system alert is audible here");
        var ringer = new SystemBellRinger();

        var exception = Record.Exception(() =>
        {
            ringer.Ring(BellSound.SystemAlert);
            ringer.Ring(BellSound.None);
        });

        Assert.Null(exception);
    }
}
