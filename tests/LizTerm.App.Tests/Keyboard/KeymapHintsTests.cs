// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using Avalonia.Input.Platform;
using LizTerm.App.Keyboard;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Keyboard;

/// <summary>The keypad's tooltips (keypad spec §5). Every expectation is formatted with an explicit
/// KeyGestureFormatInfo — Avalonia's common key names with the default modifier words — so it holds on every
/// machine; the headless platform registers a format of its own, and what it says is not ours to assert. The key
/// names ("Return", "PageUp") are Avalonia 12.1.2's, measured; a package bump that renames one shows up here.</summary>
public class KeymapHintsTests
{
    private static readonly Keymap Map = DefaultKeymap.Create(destructiveBackspace: true);
    private static readonly KeyGestureFormatInfo Words = new(new Dictionary<Key, string>());

    private static string? Hint(TerminalKey key, Keymap? map = null) => KeymapHints.Describe(map ?? Map, key, Words);

    [Theory]
    [InlineData(TerminalKey.PF1, "F1")]
    [InlineData(TerminalKey.PA1, "Alt+1")]
    [InlineData(TerminalKey.Attn, "Escape")]
    [InlineData(TerminalKey.EraseEof, "End")]
    public void A_key_with_one_chord_is_that_chord(TerminalKey key, string expected)
    {
        Assert.Equal(expected, Hint(key));
    }

    /// <summary>Unmodified first, then by modifier (Shift before Alt before Ctrl), function keys ahead of other
    /// keys within a group, taps last; two join with "or", three with a comma and "or".</summary>
    [Theory]
    [InlineData(TerminalKey.PF13, "Shift+F1 or Ctrl+F1")]
    [InlineData(TerminalKey.PA2, "Alt+2 or Ctrl+Home")]
    [InlineData(TerminalKey.PF7, "F7 or PageUp")]
    [InlineData(TerminalKey.Clear, "Pause or Ctrl+Escape")]
    [InlineData(TerminalKey.Reset, "Ctrl+R or a tap of Left Ctrl")]
    [InlineData(TerminalKey.Insert, "Insert or Ctrl+I")]
    [InlineData(TerminalKey.Enter, "Return, Ctrl+Return or a tap of Right Ctrl")]
    public void Several_chords_are_ordered_and_joined(TerminalKey key, string expected)
    {
        Assert.Equal(expected, Hint(key));
    }

    [Theory]
    [InlineData(TerminalKey.EraseInput)]
    [InlineData(TerminalKey.Dup)]
    [InlineData(TerminalKey.FieldMark)]
    public void A_key_nothing_maps_has_no_hint(TerminalKey key)
    {
        Assert.Null(Hint(key));
    }

    /// <summary>Keymap holds a Dictionary, whose enumeration order is an implementation detail; the text must not
    /// depend on it.</summary>
    [Fact]
    public void The_same_table_in_reverse_order_gives_the_same_text()
    {
        var reversed = new Keymap(Map.Keys.Reverse(), Map.Text);

        Assert.Equal(Hint(TerminalKey.Enter), Hint(TerminalKey.Enter, reversed));
        Assert.Equal(Hint(TerminalKey.PF13), Hint(TerminalKey.PF13, reversed));
        Assert.Equal(Hint(TerminalKey.PA2), Hint(TerminalKey.PA2, reversed));
    }

    /// <summary>The hook for #18: a remap through Keymap.With changes the answer, so a tooltip can never describe a
    /// binding that is gone.</summary>
    [Fact]
    public void A_remapped_key_changes_the_hint()
    {
        var remapped = Map.With([KeyValuePair.Create(new KeyChord(Key.F9), TerminalKey.PA1)], []);

        Assert.Equal("F9 or Alt+1", Hint(TerminalKey.PA1, remapped));
        Assert.Null(Hint(TerminalKey.PF9, remapped));
    }

    /// <summary>A null format means the platform's registration — under a plain [Fact] there is none, and
    /// Avalonia falls back to its invariant names. Only that it answers is asserted here.</summary>
    [Fact]
    public void A_null_format_still_answers()
    {
        Assert.NotNull(KeymapHints.Describe(Map, TerminalKey.PA1));
    }

    [Fact]
    public void Ordered_puts_unmodified_first_then_shift_alt_control_then_taps()
    {
        var chords = new[]
        {
            KeyChord.TapOf(Key.LeftCtrl),
            new KeyChord(Key.Home, KeyModifiers.Control),
            new KeyChord(Key.D2, KeyModifiers.Alt),
            new KeyChord(Key.F1, KeyModifiers.Shift),
            new KeyChord(Key.F7),
            new KeyChord(Key.PageUp),
        };

        Assert.Equal(
            [new KeyChord(Key.F7), new KeyChord(Key.PageUp), new KeyChord(Key.F1, KeyModifiers.Shift),
             new KeyChord(Key.D2, KeyModifiers.Alt), new KeyChord(Key.Home, KeyModifiers.Control), KeyChord.TapOf(Key.LeftCtrl)],
            KeymapHints.Ordered(chords));
    }

    [Fact]
    public void One_chord_reads_as_it_does_in_a_tooltip()
    {
        Assert.Equal("Alt+2", KeymapHints.Describe(new KeyChord(Key.D2, KeyModifiers.Alt), Words));
        Assert.Equal("a tap of Right Ctrl", KeymapHints.Describe(KeyChord.TapOf(Key.RightCtrl), Words));
    }

    /// <summary>The one reversal every caller reads: each key's chords, and nothing for a key nothing maps.</summary>
    [Fact]
    public void ByKey_reverses_the_table()
    {
        var byKey = KeymapHints.ByKey(Map);

        Assert.Equal([new KeyChord(Key.D2, KeyModifiers.Alt), new KeyChord(Key.Home, KeyModifiers.Control)],
            KeymapHints.Ordered(byKey[TerminalKey.PA2]));
        Assert.Empty(byKey[TerminalKey.Dup]);
    }

    private static string? MenuChord(TerminalKey key, bool isMacOS, Keymap? map = null) =>
        KeymapHints.MenuChord(KeymapHints.ByKey(map ?? Map)[key], isMacOS) is { } chord ? KeymapHints.Describe(chord, Words) : null;

    /// <summary>Keys menu shortcuts spec §3.2: one chord per item, the first in Ordered's order that is not a tap
    /// and whose key the platform's keyboard has. Both platforms are pinned from one machine: the platform is an
    /// argument.</summary>
    [Theory]
    [InlineData(TerminalKey.PF13, false, "Shift+F1")]
    [InlineData(TerminalKey.PF13, true, "Shift+F1")]
    [InlineData(TerminalKey.PF24, true, "Shift+F12")]
    [InlineData(TerminalKey.PA1, false, "Alt+1")]
    [InlineData(TerminalKey.PA2, true, "Alt+2")]
    [InlineData(TerminalKey.Reset, true, "Ctrl+R")]
    [InlineData(TerminalKey.Attn, true, "Escape")]
    [InlineData(TerminalKey.SysReq, true, "Shift+Escape")]
    [InlineData(TerminalKey.Clear, false, "Pause")]
    [InlineData(TerminalKey.Clear, true, "Ctrl+Escape")]
    [InlineData(TerminalKey.Insert, false, "Insert")]
    [InlineData(TerminalKey.Insert, true, "Ctrl+I")]
    public void The_menu_chord_is_the_first_the_platform_can_press(TerminalKey key, bool isMacOS, string expected)
    {
        Assert.Equal(expected, MenuChord(key, isMacOS));
    }

    [Theory]
    [InlineData(TerminalKey.Dup)]
    [InlineData(TerminalKey.FieldMark)]
    public void A_key_nothing_maps_has_no_menu_chord(TerminalKey key)
    {
        Assert.Null(MenuChord(key, isMacOS: false));
        Assert.Null(MenuChord(key, isMacOS: true));
    }

    [Fact]
    public void A_key_only_a_tap_sends_has_no_menu_chord()
    {
        Assert.Null(KeymapHints.MenuChord([KeyChord.TapOf(Key.LeftCtrl)], isMacOS: false));
    }

    /// <summary>A rebind wins when it sorts first: F9 is unmodified, so it beats Alt+1.</summary>
    [Fact]
    public void A_remapped_key_changes_the_menu_chord()
    {
        var remapped = Map.With([KeyValuePair.Create(new KeyChord(Key.F9), TerminalKey.PA1)], []);

        Assert.Equal("F9", MenuChord(TerminalKey.PA1, isMacOS: false, remapped));
    }

    /// <summary>The Dictionary order must not leak into which chord the menu shows.</summary>
    [Fact]
    public void The_same_table_in_reverse_order_gives_the_same_menu_chord()
    {
        var reversed = new Keymap(Map.Keys.Reverse(), Map.Text);

        Assert.Equal(MenuChord(TerminalKey.PF13, true), MenuChord(TerminalKey.PF13, true, reversed));
        Assert.Equal(MenuChord(TerminalKey.Clear, false), MenuChord(TerminalKey.Clear, false, reversed));
    }
}
