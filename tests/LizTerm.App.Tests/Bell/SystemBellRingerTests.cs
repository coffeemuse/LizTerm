// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Bell;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Bell;

/// <summary>There is no way to assert a sound, which is the point of the IBellRinger seam. What can be asserted is
/// that the platform call is reachable and does not throw here: on macOS that is NSBeep, on Windows MessageBeep,
/// on Linux the deliberate no-op.</summary>
public class SystemBellRingerTests
{
    [Fact]
    public void Ring_does_not_throw_on_this_platform()
    {
        var ringer = new SystemBellRinger();

        ringer.Ring(BellSound.SystemAlert);
        ringer.Ring(BellSound.None);
    }
}
