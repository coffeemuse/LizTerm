// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using Avalonia.Input.Platform;

namespace LizTerm.App.Keyboard;

/// <summary>What TerminalScreen keeps for itself ahead of the keymap.</summary>
public enum ReservedGesture { None, Copy, Paste, SelectAll, Find, SwitchSession }

/// <summary>The platform gestures TerminalScreen checks ahead of the keymap, as data, so the policy is the same
/// pure function on a Mac, on Windows and in a test. <see cref="Classify"/> is the one place the screen and the
/// policy both ask, so a gesture added there is refused by the tab and inert in the file alike.</summary>
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

    /// <summary>Which reserved gesture, if any, this key and these modifiers are. PlatformHotkeyConfiguration carries
    /// no Find or Switch Session, so those are the command modifier and F or K, which is Cmd on macOS and Ctrl
    /// elsewhere.</summary>
    public ReservedGesture Classify(Key key, KeyModifiers modifiers)
    {
        if (Uses(Copy, key, modifiers)) return ReservedGesture.Copy;
        if (Uses(Paste, key, modifiers)) return ReservedGesture.Paste;
        if (Uses(SelectAll, key, modifiers)) return ReservedGesture.SelectAll;
        if (modifiers == CommandModifiers && key == Key.F) return ReservedGesture.Find;
        if (modifiers == CommandModifiers && key == Key.K) return ReservedGesture.SwitchSession;
        return ReservedGesture.None;
    }

    private static bool Uses(IReadOnlyList<KeyGesture> gestures, Key key, KeyModifiers modifiers) =>
        gestures.Any(gesture => gesture.Key == key && gesture.KeyModifiers == modifiers);

    /// <summary>TerminalScreen used to fall back to Ctrl+key when the platform lists nothing; so does this.</summary>
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
/// system sees it first; and a printable key with no modifier, Shift alone, or, off macOS, Ctrl+Alt (AltGr on
/// Windows and Linux), because there would be no way to type that character afterwards. Everything else is
/// allowed: a chord another action holds moves (the tab says from where), and unbinding a default is silent. "Printable" is decided by the Key value alone, not by asking
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
        switch (hotkeys.Classify(chord.Key, chord.Modifiers))
        {
            case ReservedGesture.Copy: return Reserved("Copy");
            case ReservedGesture.Paste: return Reserved("Paste");
            case ReservedGesture.SelectAll: return Reserved("Select All");
            case ReservedGesture.Find: return Reserved("Find");
            case ReservedGesture.SwitchSession: return Reserved("Switch Session");
        }
        if (chord.Modifiers.HasFlag(KeyModifiers.Meta))
        {
            return new KeymapVerdict.Refused(hotkeys.CommandModifiers.HasFlag(KeyModifiers.Meta)
                ? "The menu bar sees Cmd shortcuts before the screen does"
                : "The system sees Windows key shortcuts before the screen does");
        }
        if (Printable.Contains(chord.Key) && chord.Modifiers is KeyModifiers.None or KeyModifiers.Shift)
            return new KeymapVerdict.Refused("This would take away typing that character");
        // AltGr arrives as Ctrl+Alt on Windows and Linux, and types a character on many layouts (Ctrl+Alt+Q is @ on a
        // German one). macOS has no AltGr: Ctrl+Option+letter types nothing there, so it stays a chord. The platform
        // is told apart the way the Cmd wording is, by which key the platform calls its command modifier.
        if (Printable.Contains(chord.Key) && !hotkeys.CommandModifiers.HasFlag(KeyModifiers.Meta)
            && chord.Modifiers.HasFlag(KeyModifiers.Control) && chord.Modifiers.HasFlag(KeyModifiers.Alt))
            return new KeymapVerdict.Refused("AltGr types this character on some keyboards");
        return KeymapVerdict.Allowed.Instance;
    }

    private static KeymapVerdict Reserved(string use) => new KeymapVerdict.Refused($"LizTerm uses this for {use}");
}
