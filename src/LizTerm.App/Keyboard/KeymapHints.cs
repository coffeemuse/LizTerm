// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using LizTerm.Core.Session;

namespace LizTerm.App.Keyboard;

/// <summary>The keyboard equivalents of a key as one line of text, for the keypad's tooltips (keypad spec §5) and
/// the Keyboard tab's chips, and as one chord for the Keys menu's shortcut (Keys menu shortcuts spec §3.2). Pure:
/// the keymap, the platform and the format are arguments, so a remap (#18) changes the answer and a test can pin
/// the words.</summary>
public static class KeymapHints
{
    /// <summary>The keymap reversed: every chord that sends each key. The one reversal the keypad, the Keys menu
    /// and the single-key Describe all read, so what counts as a chord for a key is decided here alone.</summary>
    public static ILookup<TerminalKey, KeyChord> ByKey(Keymap keymap) =>
        keymap.Keys.ToLookup(pair => pair.Value, pair => pair.Key);

    /// <summary>A Keys menu item's header (editable keymap spec §6.2, #23): the name, two spaces, then Describe's
    /// line for the chords, so a menu item, a tooltip and a Keyboard tab chip cannot disagree; a key no chord sends
    /// keeps its bare name. Header text, never a gesture: see the menu notes in CLAUDE.md.</summary>
    public static string Label(string name, IEnumerable<KeyChord> chords, IFormatProvider? format = null) =>
        Describe(chords, format) is { } hint ? name + "  " + hint : name;

    /// <summary>Every chord in the keymap that sends the key, or null when none does. Ordered without reference to
    /// the table's insertion order (Keymap holds a Dictionary): unmodified chords first, then by KeyModifiers value
    /// (Alt, Control, Shift, combinations after), function keys ahead of other keys within a group, taps last.
    /// Ordinary chords are formatted by Avalonia's own platform formatter — glyphs on macOS, words elsewhere — and a
    /// null format means the platform's registration; taps, which it has no word for, are worded here.</summary>
    public static string? Describe(Keymap keymap, TerminalKey key, IFormatProvider? format = null) =>
        Describe(ByKey(keymap)[key], format);

    /// <summary>The same line from chords already collected, for a caller that reverses the keymap once and then
    /// asks about many keys: the keypad's 36 tooltips are one lookup rather than 36 scans of the table.</summary>
    public static string? Describe(IEnumerable<KeyChord> chords, IFormatProvider? format = null)
    {
        var formatted = Ordered(chords).Select(chord => Format(chord, format)).ToList();
        return formatted.Count switch
        {
            0 => null,
            1 => formatted[0],
            _ => string.Join(", ", formatted.Take(formatted.Count - 1)) + " or " + formatted[^1],
        };
    }

    /// <summary>One chord as text, worded exactly as it is inside a tooltip's line, for the Keyboard tab's chips.</summary>
    public static string Describe(KeyChord chord, IFormatProvider? format = null) => Format(chord, format);

    /// <summary>The order every list of chords is shown in, tooltips, the Keyboard tab and the Keys menu alike:
    /// unmodified chords first, then Shift, Alt, Control, then combinations; function keys ahead of other keys
    /// within a group; taps last. Shift+F1 ahead of Ctrl+F1 is the order every 3270 user is taught PF13 in.</summary>
    public static IEnumerable<KeyChord> Ordered(IEnumerable<KeyChord> chords) => chords
        .OrderBy(chord => chord.Tap ? 1 : 0)
        .ThenBy(chord => ModifierRank(chord.Modifiers))
        .ThenBy(chord => IsFunctionKey(chord.Key) ? 0 : 1)
        .ThenBy(chord => (int)chord.Key);

    /// <summary>The one chord a Keys menu item shows (Keys menu shortcuts spec §3.2): the first in Ordered's order
    /// that is not a tap and whose key the platform's keyboard has. Apple keyboards have no Pause and no Insert.
    /// Null when nothing qualifies, and the item shows no shortcut.</summary>
    public static KeyChord? MenuChord(IEnumerable<KeyChord> chords, bool isMacOS)
    {
        foreach (var chord in Ordered(chords.Where(chord => !chord.Tap && !(isMacOS && chord.Key is Key.Pause or Key.Insert))))
            return chord;
        return null;
    }

    private static int ModifierRank(KeyModifiers modifiers) => modifiers switch
    {
        KeyModifiers.None => 0,
        KeyModifiers.Shift => 1,
        KeyModifiers.Alt => 2,
        KeyModifiers.Control => 3,
        _ => 4 + (int)modifiers,
    };

    private static bool IsFunctionKey(Key key) => key is >= Key.F1 and <= Key.F24;

    private static string Format(KeyChord chord, IFormatProvider? format) => chord switch
    {
        { Tap: true, Key: Key.LeftCtrl } => "a tap of Left Ctrl",
        { Tap: true, Key: Key.RightCtrl } => "a tap of Right Ctrl",
        { Tap: true } => "a tap of " + new KeyGesture(chord.Key).ToString("p", format),
        _ => new KeyGesture(chord.Key, chord.Modifiers).ToString("p", format),
    };
}
