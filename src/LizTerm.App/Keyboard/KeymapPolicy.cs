// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using Avalonia.Input.Platform;

namespace LizTerm.App.Keyboard;

/// <summary>The platform gestures TerminalScreen checks ahead of the keymap, as data, so the policy is the same
/// pure function on a Mac, on Windows and in a test.</summary>
public sealed record PlatformHotkeys(
    IReadOnlyList<KeyGesture> Copy,
    IReadOnlyList<KeyGesture> Paste,
    IReadOnlyList<KeyGesture> SelectAll,
    KeyModifiers CommandModifiers)
{
    /// <summary>What TerminalScreen.Matches falls back to when the platform answers nothing: Ctrl+C, Ctrl+V,
    /// Ctrl+A, and Ctrl as the command modifier. Windows- and Linux-shaped.</summary>
    public static PlatformHotkeys Fallback { get; } = new(
        [new KeyGesture(Key.C, KeyModifiers.Control)],
        [new KeyGesture(Key.V, KeyModifiers.Control)],
        [new KeyGesture(Key.A, KeyModifiers.Control)],
        KeyModifiers.Control);

    /// <summary>What macOS answers, for tests: Cmd on everything, and Ctrl+Insert still a Copy gesture (which is
    /// why PA1 is not on it; see DefaultKeymap).</summary>
    public static PlatformHotkeys MacOS { get; } = new(
        [new KeyGesture(Key.C, KeyModifiers.Meta), new KeyGesture(Key.Insert, KeyModifiers.Control)],
        [new KeyGesture(Key.V, KeyModifiers.Meta)],
        [new KeyGesture(Key.A, KeyModifiers.Meta)],
        KeyModifiers.Meta);

    public static PlatformHotkeys From(PlatformHotkeyConfiguration? configuration) =>
        configuration is null
            ? Fallback
            : new(OrFallback(configuration.Copy, Fallback.Copy), OrFallback(configuration.Paste, Fallback.Paste),
                  OrFallback(configuration.SelectAll, Fallback.SelectAll), configuration.CommandModifiers);

    /// <summary>TerminalScreen.Matches falls back to Ctrl+key when the platform lists nothing; so does this.</summary>
    private static IReadOnlyList<KeyGesture> OrFallback(IReadOnlyList<KeyGesture> gestures, IReadOnlyList<KeyGesture> fallback) =>
        gestures.Count > 0 ? gestures : fallback;
}

/// <summary>The answer to "may this chord be bound": Allowed, or Refused with the reason the tab shows verbatim.</summary>
public abstract record KeymapVerdict
{
    private KeymapVerdict() { }

    public sealed record Allowed : KeymapVerdict
    {
        public static readonly Allowed Instance = new();
    }

    public sealed record Refused(string Reason) : KeymapVerdict;
}

/// <summary>Editable keymap spec §4: what the Keyboard tab will not bind. The platform gestures the screen checks
/// first, because a binding there would never fire; any Cmd or Windows-key chord, because the menu bar or the
/// system sees it first; and a printable key with no modifier or Shift alone, because there would be no way to
/// type that character afterwards. Everything else is allowed: a chord another action holds moves (the tab says
/// from where), and unbinding a default is silent. "Printable" is decided by the Key value alone, not by asking
/// the platform what it would type: the answer has to be the same in a test as on a Mac.</summary>
public static class KeymapPolicy
{
    private static readonly HashSet<Key> Printable =
    [
        .. Enumerable.Range((int)Key.A, 26).Select(i => (Key)i),
        .. Enumerable.Range((int)Key.D0, 10).Select(i => (Key)i),
        .. Enumerable.Range((int)Key.NumPad0, 10).Select(i => (Key)i),
        Key.Space, Key.Decimal, Key.Add, Key.Subtract, Key.Multiply, Key.Divide,
        Key.OemSemicolon, Key.OemPlus, Key.OemComma, Key.OemMinus, Key.OemPeriod, Key.OemQuestion, Key.OemTilde,
        Key.OemOpenBrackets, Key.OemPipe, Key.OemCloseBrackets, Key.OemQuotes, Key.Oem8, Key.OemBackslash,
    ];

    public static KeymapVerdict Check(KeyChord chord, PlatformHotkeys hotkeys)
    {
        if (chord.Tap) return KeymapVerdict.Allowed.Instance;
        if (Uses(hotkeys.Copy, chord)) return Reserved("Copy");
        if (Uses(hotkeys.Paste, chord)) return Reserved("Paste");
        if (Uses(hotkeys.SelectAll, chord)) return Reserved("Select All");
        if (chord.Modifiers == hotkeys.CommandModifiers && chord.Key == Key.F) return Reserved("Find");
        if (chord.Modifiers == hotkeys.CommandModifiers && chord.Key == Key.K) return Reserved("Switch Session");
        if (chord.Modifiers.HasFlag(KeyModifiers.Meta))
        {
            return new KeymapVerdict.Refused(hotkeys.CommandModifiers.HasFlag(KeyModifiers.Meta)
                ? "The menu bar sees Cmd shortcuts before the screen does"
                : "The system sees Windows key shortcuts before the screen does");
        }
        if (Printable.Contains(chord.Key) && chord.Modifiers is KeyModifiers.None or KeyModifiers.Shift)
            return new KeymapVerdict.Refused("This would take away typing that character");
        return KeymapVerdict.Allowed.Instance;
    }

    private static KeymapVerdict Reserved(string use) => new KeymapVerdict.Refused($"LizTerm uses this for {use}");

    private static bool Uses(IReadOnlyList<KeyGesture> gestures, KeyChord chord) =>
        gestures.Any(gesture => gesture.Key == chord.Key && gesture.KeyModifiers == chord.Modifiers);
}
