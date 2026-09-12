// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Settings;

namespace LizTerm.App.ViewModels;

/// <summary>"Is the dock this one?", for the View &gt; Keypad submenu's and the Preferences window's check marks
/// (keypad spec §7, #71). EnumIsConverter holds the rule, as it does for the crosshair and the bell.</summary>
public sealed class KeypadDockConverter : EnumIsConverter<KeypadDock>
{
    public static readonly KeypadDockConverter Instance = new();
}
