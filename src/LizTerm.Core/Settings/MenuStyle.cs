// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Settings;

/// <summary>Where a session window's menu is drawn. Written to the settings file by name; a member an older build
/// does not know falls to Auto for that key alone.</summary>
public enum MenuStyle
{
    /// <summary>No choice made, which is what an untouched settings file reads as: the platform decides, and
    /// MenuStrategy.Resolve is where that happens. Never a value the app acts on directly.</summary>
    Auto,
    /// <summary>The menu bar the platform draws itself — the system menu bar on macOS.</summary>
    Native,
    /// <summary>The menu drawn inside the window.</summary>
    InWindow,
    /// <summary>Both at once. Only meaningful on macOS, where the two draw in different places.</summary>
    Both,
}
