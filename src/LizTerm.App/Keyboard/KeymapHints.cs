// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using LizTerm.Core.Session;

namespace LizTerm.App.Keyboard;

/// <summary>The keyboard equivalents of a key as one line of text, for the keypad's tooltips (keypad spec §5) and,
/// one day, the Keys menu (#23). Pure: the keymap and the format are arguments, so a remap (#18) changes the
/// answer and a test can pin the words.</summary>
public static class KeymapHints
{
    /// <summary>Every chord in the keymap that sends the key, or null when none does. Ordered without reference to
    /// the table's insertion order (Keymap holds a Dictionary): unmodified chords first, then by KeyModifiers value
    /// (Alt, Control, Shift, combinations after), function keys ahead of other keys within a group, taps last.
    /// Ordinary chords are formatted by Avalonia's own platform formatter — glyphs on macOS, words elsewhere — and a
    /// null format means the platform's registration; taps, which it has no word for, are worded here.</summary>
    public static string? Describe(Keymap keymap, TerminalKey key, IFormatProvider? format = null)
    {
        var chords = keymap.Keys
            .Where(pair => pair.Value == key)
            .Select(pair => pair.Key)
            .OrderBy(chord => chord.Tap ? 1 : 0)
            .ThenBy(chord => (int)chord.Modifiers)
            .ThenBy(chord => IsFunctionKey(chord.Key) ? 0 : 1)
            .ThenBy(chord => (int)chord.Key)
            .Select(chord => Format(chord, format))
            .ToList();
        return chords.Count switch
        {
            0 => null,
            1 => chords[0],
            _ => string.Join(", ", chords.Take(chords.Count - 1)) + " or " + chords[^1],
        };
    }

    private static bool IsFunctionKey(Key key) => key is >= Key.F1 and <= Key.F24;

    private static string Format(KeyChord chord, IFormatProvider? format) => chord switch
    {
        { Tap: true, Key: Key.LeftCtrl } => "a tap of Left Ctrl",
        { Tap: true, Key: Key.RightCtrl } => "a tap of Right Ctrl",
        { Tap: true } => "a tap of " + new KeyGesture(chord.Key).ToString("p", format),
        _ => new KeyGesture(chord.Key, chord.Modifiers).ToString("p", format),
    };
}
