// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Bell;

namespace LizTerm.App.Tests.Bell;

/// <summary>The platform rule for the Preferences radio, pure and taking the platform as an argument the way
/// MenuStrategy does, so every combination is reachable on every machine.</summary>
public class BellSupportTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    public void The_system_alert_exists_on_macOS_and_Windows_only(bool isMacOS, bool isWindows, bool expected)
    {
        Assert.Equal(expected, BellSupport.SystemAlertAvailable(isMacOS, isWindows));
    }
}
