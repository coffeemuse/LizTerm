// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using LizTerm.Core.Session;

namespace LizTerm.App.Keyboard;

/// <summary>The built-in defaults: Vista TN3270's table, cross-checked against wc3270 (spec 6.2). Clear has a
/// second home on Ctrl+Escape because Mac keyboards have no Pause key, PA2 and PA3 a second home on Alt+2 and Alt+3
/// and Insert one on Ctrl+I (#111) because Mac laptops have no Insert key, and Reset a second home on Ctrl+R
/// (wc3270's) in case a platform never reports the Left Ctrl tap. Ctrl+I is Tab only to an ASCII terminal; a 3270
/// host sees EBCDIC and 3270 keys, never ASCII control codes, so a TN3270 client need not reserve it. Copy, paste,
/// and select-all are platform hotkeys checked before this table and are deliberately absent from it; that includes
/// Vista's Ctrl+Insert for PA1, which Avalonia lists as a Copy gesture on every platform (the Meta-based macOS table
/// included), so PA1 lives on Alt+1 alone. Not user-editable yet; see <see cref="Keymap.With"/>.</summary>
public static class DefaultKeymap
{
    private static readonly Keymap Erasing = Build(destructiveBackspace: true);
    private static readonly Keymap CursorLeft = Build(destructiveBackspace: false);

    /// <param name="destructiveBackspace">The profile's choice: Backspace as <see cref="TerminalKey.Erase"/> (true,
    /// the default) or as the cursor-left <see cref="TerminalKey.Backspace"/>.</param>
    public static Keymap Create(bool destructiveBackspace) => destructiveBackspace ? Erasing : CursorLeft;

    private static Keymap Build(bool destructiveBackspace)
    {
        var keys = new List<KeyValuePair<KeyChord, TerminalKey>>();
        void Add(Key key, TerminalKey terminal, KeyModifiers modifiers = KeyModifiers.None) =>
            keys.Add(KeyValuePair.Create(new KeyChord(key, modifiers), terminal));
        void Tap(Key key, TerminalKey terminal) => keys.Add(KeyValuePair.Create(KeyChord.TapOf(key), terminal));

        Add(Key.Enter, TerminalKey.Enter);
        Add(Key.Enter, TerminalKey.Enter, KeyModifiers.Control);
        Tap(Key.RightCtrl, TerminalKey.Enter);
        Add(Key.Enter, TerminalKey.Newline, KeyModifiers.Shift);
        Add(Key.Escape, TerminalKey.Attn);
        Add(Key.Escape, TerminalKey.SysReq, KeyModifiers.Shift);
        Tap(Key.LeftCtrl, TerminalKey.Reset);
        Add(Key.R, TerminalKey.Reset, KeyModifiers.Control);
        Add(Key.Pause, TerminalKey.Clear);
        Add(Key.Escape, TerminalKey.Clear, KeyModifiers.Control);
        for (var i = 0; i < 12; i++)
        {
            Add(Key.F1 + i, TerminalKey.PF1 + i);
            Add(Key.F1 + i, TerminalKey.PF13 + i, KeyModifiers.Shift);
            Add(Key.F1 + i, TerminalKey.PF13 + i, KeyModifiers.Control);
        }
        Add(Key.PageUp, TerminalKey.PF7);
        Add(Key.PageDown, TerminalKey.PF8);
        Add(Key.Home, TerminalKey.PA2, KeyModifiers.Control);
        Add(Key.PageUp, TerminalKey.PA3, KeyModifiers.Control);
        Add(Key.D1, TerminalKey.PA1, KeyModifiers.Alt);
        Add(Key.D2, TerminalKey.PA2, KeyModifiers.Alt);
        Add(Key.D3, TerminalKey.PA3, KeyModifiers.Alt);
        Add(Key.Tab, TerminalKey.Tab);
        Add(Key.Tab, TerminalKey.BackTab, KeyModifiers.Shift);
        Add(Key.Insert, TerminalKey.Insert);
        Add(Key.I, TerminalKey.Insert, KeyModifiers.Control);
        Add(Key.Home, TerminalKey.Home);
        Add(Key.End, TerminalKey.EraseEof);
        Add(Key.Delete, TerminalKey.Delete);
        Add(Key.Back, destructiveBackspace ? TerminalKey.Erase : TerminalKey.Backspace);
        Add(Key.Up, TerminalKey.Up);
        Add(Key.Down, TerminalKey.Down);
        Add(Key.Left, TerminalKey.Left);
        Add(Key.Right, TerminalKey.Right);

        var text = new List<KeyValuePair<KeyChord, string>>
        {
            KeyValuePair.Create(new KeyChord(Key.OemOpenBrackets, KeyModifiers.Control), "¬"),   // Vista: Ctrl+[ is the NOT sign
            KeyValuePair.Create(new KeyChord(Key.D6, KeyModifiers.Control), "¢"),                // Vista: Ctrl+6 is the cent sign
        };
        return new Keymap(keys, text);
    }
}
