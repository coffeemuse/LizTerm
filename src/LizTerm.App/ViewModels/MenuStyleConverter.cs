// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Settings;

namespace LizTerm.App.ViewModels;

/// <summary>"Is the menu drawn this way?", for the Preferences window's check marks. The rules, and the reasons
/// for them, are EnumIsConverter's.</summary>
public sealed class MenuStyleConverter : EnumIsConverter<MenuStyle>
{
    public static readonly MenuStyleConverter Instance = new();
}
