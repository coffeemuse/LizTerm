// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Settings;

/// <summary>Which edge of the session window the on-screen keypad docks to (keypad spec §2.1). Written by name in
/// settings.json, so a reordering can never change a saved meaning; a name this build does not know reads as
/// Bottom for that key alone (SettingsLayers.Read drops the one key it cannot parse).</summary>
public enum KeypadDock { Bottom, Right }
