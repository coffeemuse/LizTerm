// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using Avalonia.Input.Platform;
using LizTerm.App.Keyboard;
using LizTerm.App.Menus;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Menus;

/// <summary>Editable keymap spec §6.2: a Keys item's header is its name, two spaces, then the keypad tooltip's own
/// line for that key; a key with no chord keeps its bare name. Formatted with an explicit KeyGestureFormatInfo for
/// the reason KeymapHintsTests gives: the headless platform's own wording is not ours to assert.</summary>
public class KeysMenuHintsTests
{
    private static readonly Keymap Map = DefaultKeymap.Create(destructiveBackspace: true);
    private static readonly KeyGestureFormatInfo Words = new(new Dictionary<Key, string>());

    private static IEnumerable<KeyChord> ChordsFor(TerminalKey key) =>
        Map.Keys.Where(pair => pair.Value == key).Select(pair => pair.Key);

    [Fact]
    public void A_key_with_chords_reads_its_name_two_spaces_and_the_tooltips_line()
    {
        Assert.Equal("PA2  Alt+2 or Ctrl+Home", KeysMenuHints.Header("PA2", ChordsFor(TerminalKey.PA2), Words));
        Assert.Equal("Attn  Escape", KeysMenuHints.Header("Attn", ChordsFor(TerminalKey.Attn), Words));
    }

    [Fact]
    public void A_key_with_no_chord_keeps_its_bare_name()
    {
        Assert.Equal("Field Mark", KeysMenuHints.Header("Field Mark", ChordsFor(TerminalKey.FieldMark), Words));
        Assert.Equal("Dup", KeysMenuHints.Header("Dup", [], Words));
    }

    /// <summary>The wording is KeymapHints' and nothing else, so a chip, a tooltip and a menu item cannot disagree.</summary>
    [Fact]
    public void The_hint_is_the_tooltips_own_wording()
    {
        var expected = "Insert  " + KeymapHints.Describe(Map, TerminalKey.Insert, Words);

        Assert.Equal(expected, KeysMenuHints.Header("Insert", ChordsFor(TerminalKey.Insert), Words));
    }
}
