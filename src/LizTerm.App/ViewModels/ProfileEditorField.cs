// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.ViewModels;

/// <summary>The field a profile editor validation message is about. The window shows that field's tab when Save is
/// refused and outlines the field, so a message never names something the user cannot see.</summary>
public enum ProfileEditorField
{
    Name,
    Host,
    Port,
    KeepAlive,
    ScreenSize,
    CodePage,
    Tags,
}
