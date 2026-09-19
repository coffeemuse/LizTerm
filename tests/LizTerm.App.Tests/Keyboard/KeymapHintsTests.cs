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

    /// <summary>Unmodified first, then by modifier (Alt before Ctrl before Shift), function keys ahead of other
    /// keys within a group, taps last; two join with "or", three with a comma and "or".</summary>
    [Theory]
    [InlineData(TerminalKey.PF13, "Ctrl+F1 or Shift+F1")]
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
    public void Ordered_puts_unmodified_first_then_modifiers_then_taps()
    {
        var chords = new[]
        {
            KeyChord.TapOf(Key.LeftCtrl),
            new KeyChord(Key.Home, KeyModifiers.Control),
            new KeyChord(Key.D2, KeyModifiers.Alt),
            new KeyChord(Key.F7),
            new KeyChord(Key.PageUp),
        };

        Assert.Equal(
            [new KeyChord(Key.F7), new KeyChord(Key.PageUp), new KeyChord(Key.D2, KeyModifiers.Alt),
             new KeyChord(Key.Home, KeyModifiers.Control), KeyChord.TapOf(Key.LeftCtrl)],
            KeymapHints.Ordered(chords));
    }

    [Fact]
    public void One_chord_reads_as_it_does_in_a_tooltip()
    {
        Assert.Equal("Alt+2", KeymapHints.Describe(new KeyChord(Key.D2, KeyModifiers.Alt), Words));
        Assert.Equal("a tap of Right Ctrl", KeymapHints.Describe(KeyChord.TapOf(Key.RightCtrl), Words));
    }
}
