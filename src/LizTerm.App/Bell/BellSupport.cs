// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Bell;

/// <summary>Which bell sounds this platform can make. Pure, taking the platform rather than reading it, the shape
/// MenuStrategy.AboutInHelpMenu uses so every combination is testable on every machine.</summary>
internal static class BellSupport
{
    /// <summary>NSBeep and MessageBeep always exist; Linux has no guaranteed audio path without a library
    /// dependency (#47), so the Preferences radio is disabled there and says why.</summary>
    public static bool SystemAlertAvailable(bool isMacOS, bool isWindows) => isMacOS || isWindows;

    public static bool SystemAlertAvailableHere => SystemAlertAvailable(OperatingSystem.IsMacOS(), OperatingSystem.IsWindows());
}
