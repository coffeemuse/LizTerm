// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using LizTerm.App.Keyboard;

namespace LizTerm.App.Tests.Keyboard;

/// <summary>keymap.json's chord spelling (editable keymap spec §3.1): LizTerm's own, case-insensitive in, fixed
/// modifier order out, and never Cmd.</summary>
public class ChordSyntaxTests
{
    public static TheoryData<string, Key, KeyModifiers> Spellings => new()
    {
        { "F1", Key.F1, KeyModifiers.None },
        { "Ctrl+Home", Key.Home, KeyModifiers.Control },
        { "ctrl+shift+f1", Key.F1, KeyModifiers.Control | KeyModifiers.Shift },
        { "Shift+Ctrl+F1", Key.F1, KeyModifiers.Control | KeyModifiers.Shift },
        { " Alt + D1 ", Key.D1, KeyModifiers.Alt },
        { "Ctrl+OemOpenBrackets", Key.OemOpenBrackets, KeyModifiers.Control },
        { "Ctrl+Alt+Shift+A", Key.A, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift },
    };

    [Theory]
    [MemberData(nameof(Spellings))]
    public void Parses_a_chord(string text, Key key, KeyModifiers modifiers)
    {
        Assert.True(ChordSyntax.TryParse(text, out var chord));
        Assert.Equal(new KeyChord(key, modifiers), chord);
    }

    [Theory]
    [InlineData("Tap:LeftCtrl", Key.LeftCtrl)]
    [InlineData("tap:rightctrl", Key.RightCtrl)]
    public void Parses_a_tap(string text, Key key)
    {
        Assert.True(ChordSyntax.TryParse(text, out var chord));
        Assert.Equal(KeyChord.TapOf(key), chord);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+")]
    [InlineData("+F1")]
    [InlineData("Cmd+K")]
    [InlineData("Meta+K")]
    [InlineData("Win+K")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Ctrl+3")]
    [InlineData("Ctrl+Home,End")]
    [InlineData("Ctrl+ 3")]
    [InlineData("Ctrl+-1")]
    [InlineData("Ctrl+None")]
    [InlineData("Ctrl+Nonsense")]
    [InlineData("LeftCtrl")]
    [InlineData("Ctrl+LeftShift")]
    [InlineData("Tap:A")]
    [InlineData("Tap:LeftShift")]
    [InlineData("Tap:")]
    public void Rejects_what_is_not_a_chord(string text)
    {
        Assert.False(ChordSyntax.TryParse(text, out _));
    }

    [Fact]
    public void Formats_modifiers_in_a_fixed_order()
    {
        Assert.Equal("Ctrl+Alt+Shift+A", ChordSyntax.Format(new KeyChord(Key.A, KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Control)));
        Assert.Equal("Home", ChordSyntax.Format(new KeyChord(Key.Home)));
        Assert.Equal("Tap:RightCtrl", ChordSyntax.Format(KeyChord.TapOf(Key.RightCtrl)));
    }

    [Fact]
    public void A_Cmd_chord_formats_but_never_parses()
    {
        var formatted = ChordSyntax.Format(new KeyChord(Key.K, KeyModifiers.Meta));

        Assert.Equal("Cmd+K", formatted);
        Assert.False(ChordSyntax.TryParse(formatted, out _));
    }

    [Fact]
    public void Every_default_chord_round_trips()
    {
        var map = DefaultKeymap.Create(destructiveBackspace: true);
        foreach (var chord in map.Keys.Keys.Concat(map.Text.Keys))
        {
            Assert.True(ChordSyntax.TryParse(ChordSyntax.Format(chord), out var back), ChordSyntax.Format(chord));
            Assert.Equal(chord, back);
        }
    }

    [Theory]
    [InlineData(Key.LeftCtrl, true)]
    [InlineData(Key.RightShift, true)]
    [InlineData(Key.LeftAlt, true)]
    [InlineData(Key.LWin, true)]
    [InlineData(Key.Home, false)]
    [InlineData(Key.A, false)]
    public void IsModifierKey_names_the_keys_that_are_only_modifiers(Key key, bool expected)
    {
        Assert.Equal(expected, ChordSyntax.IsModifierKey(key));
    }
}
