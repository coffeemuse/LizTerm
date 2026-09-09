// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;

namespace LizTerm.App.Keyboard;

/// <summary>One keyboard gesture: a key with its modifiers, or a modifier key pressed and released alone
/// (<paramref name="Tap"/>). The dictionary key of a <see cref="Keymap"/>.</summary>
public readonly record struct KeyChord(Key Key, KeyModifiers Modifiers = KeyModifiers.None, bool Tap = false)
{
    public static KeyChord TapOf(Key key) => new(key, KeyModifiers.None, Tap: true);
}
