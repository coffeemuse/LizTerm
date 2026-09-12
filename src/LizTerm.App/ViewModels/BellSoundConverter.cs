// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Settings;

namespace LizTerm.App.ViewModels;

/// <summary>"Is the bell sound this one?", for a Preferences radio's IsChecked. The rules, and the reasons for
/// them, are EnumIsConverter's.</summary>
public sealed class BellSoundConverter : EnumIsConverter<BellSound>
{
    public static readonly BellSoundConverter Instance = new();
}
