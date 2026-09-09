// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;

namespace LizTerm.App.Keyboard;

/// <summary>Spec 6.3. A tap is a Left or Right Ctrl key going down and the same key coming up with no other key
/// pressed in between, so Ctrl+C is never a Reset: the C press clears the record. Auto-repeat of the same Ctrl
/// key keeps it. Pure; the screen control feeds it from its key events and resets it on focus loss.</summary>
public sealed class ModifierTapDetector
{
    private Key? _candidate;

    public void KeyDown(Key key) => _candidate = key is Key.LeftCtrl or Key.RightCtrl ? key : null;

    /// <returns>The tapped key, or null when this release is not a tap.</returns>
    public Key? KeyUp(Key key)
    {
        var tapped = _candidate == key ? key : (Key?)null;
        _candidate = null;
        return tapped;
    }

    public void Reset() => _candidate = null;
}
