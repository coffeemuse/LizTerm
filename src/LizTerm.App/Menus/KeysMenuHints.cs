// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Keyboard;

namespace LizTerm.App.Menus;

/// <summary>What a Keys menu item reads (editable keymap spec §6.2, #23): the action's name, two spaces, then the
/// keystrokes that send it in KeymapHints' wording, the keypad tooltip's own line, so a menu item, a tooltip and a
/// Keyboard tab chip cannot disagree. A key no chord sends keeps its bare name. In the header text on purpose, never
/// a gesture: on the native menu a gesture is an AppKit key equivalent that takes the keystroke away from the screen,
/// and header text is the one form both renderers draw alike, which is what keeps the parity test honest.</summary>
internal static class KeysMenuHints
{
    public static string Header(string name, IEnumerable<KeyChord> chords, IFormatProvider? format = null) =>
        KeymapHints.Describe(chords, format) is { } hint ? name + "  " + hint : name;
}
