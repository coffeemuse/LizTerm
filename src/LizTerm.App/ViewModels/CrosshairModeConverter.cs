// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Settings;

namespace LizTerm.App.ViewModels;

/// <summary>"Is the crosshair in this mode?", for the View menu's and the Preferences window's check marks. The
/// rules, and the reasons for them, are EnumIsConverter's.</summary>
public sealed class CrosshairModeConverter : EnumIsConverter<CrosshairMode>
{
    public static readonly CrosshairModeConverter Instance = new();
}
