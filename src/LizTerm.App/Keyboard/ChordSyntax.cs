// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;

namespace LizTerm.App.Keyboard;

/// <summary>keymap.json's spelling of a chord (editable keymap spec §3.1): the modifiers in the fixed order Ctrl,
/// Alt, Shift joined with "+" to an Avalonia Key name (Ctrl+Shift+F1, Ctrl+OemOpenBrackets), or "Tap:" and a Ctrl
/// key (Tap:LeftCtrl). Case does not matter on the way in. LizTerm's own rather than KeyGesture.Parse: Avalonia's
/// spelling is platform-flavoured and admits Cmd, which KeymapPolicy refuses, so a Meta chord formats (as Cmd, for
/// a message) but never parses.</summary>
public static class ChordSyntax
{
    private const string TapPrefix = "Tap:";

    public static string Format(KeyChord chord)
    {
        if (chord.Tap) return TapPrefix + chord.Key;
        var parts = new List<string>(4);
        if (chord.Modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (chord.Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (chord.Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (chord.Modifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Cmd");
        parts.Add(chord.Key.ToString());
        return string.Join('+', parts);
    }

    public static bool TryParse(string text, out KeyChord chord)
    {
        chord = default;
        var trimmed = text.Trim();
        if (trimmed.StartsWith(TapPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryKey(trimmed[TapPrefix.Length..], out var tapped) || !IsTappable(tapped)) return false;
            chord = KeyChord.TapOf(tapped);
            return true;
        }

        var parts = trimmed.Split('+');
        var modifiers = KeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var modifier = parts[i].Trim().ToLowerInvariant() switch
            {
                "ctrl" => KeyModifiers.Control,
                "alt" => KeyModifiers.Alt,
                "shift" => KeyModifiers.Shift,
                _ => KeyModifiers.None,
            };
            if (modifier == KeyModifiers.None || modifiers.HasFlag(modifier)) return false;
            modifiers |= modifier;
        }
        if (!TryKey(parts[^1].Trim(), out var key) || IsModifierKey(key)) return false;
        chord = new KeyChord(key, modifiers);
        return true;
    }

    /// <summary>A Key by name: defined, not None, and not a number, a sign or a comma list (Enum.TryParse would
    /// accept "3", "+1" and "Home,End"). A name is a letter, then letters and digits.</summary>
    private static bool TryKey(string name, out Key key)
    {
        key = Key.None;
        return name.Length > 0 && char.IsAsciiLetter(name[0]) && name.All(char.IsAsciiLetterOrDigit)
               && Enum.TryParse(name, ignoreCase: true, out key) && Enum.IsDefined(key) && key != Key.None;
    }

    /// <summary>The keys ModifierTapDetector watches; a tap of anything else is not a chord.</summary>
    private static bool IsTappable(Key key) => key is Key.LeftCtrl or Key.RightCtrl;

    /// <summary>The keys that are modifiers and nothing else; a chord's key is never one of them.</summary>
    public static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;
}
