// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using LizTerm.App.Keyboard;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Keyboard;

/// <summary>Every row of the cross-check table in spec 6.2, plus the seam for remapping.</summary>
public class KeymapTests
{
    private static readonly Keymap Map = DefaultKeymap.Create(destructiveBackspace: true);

    public static TheoryData<Key, KeyModifiers, TerminalKey> Rows => new()
    {
        { Key.Enter, KeyModifiers.None, TerminalKey.Enter },
        { Key.Enter, KeyModifiers.Control, TerminalKey.Enter },
        { Key.Enter, KeyModifiers.Shift, TerminalKey.Newline },
        { Key.Escape, KeyModifiers.None, TerminalKey.Attn },
        { Key.Escape, KeyModifiers.Shift, TerminalKey.SysReq },
        { Key.Escape, KeyModifiers.Control, TerminalKey.Clear },
        { Key.Pause, KeyModifiers.None, TerminalKey.Clear },
        { Key.R, KeyModifiers.Control, TerminalKey.Reset },
        { Key.F1, KeyModifiers.None, TerminalKey.PF1 },
        { Key.F12, KeyModifiers.None, TerminalKey.PF12 },
        { Key.F1, KeyModifiers.Shift, TerminalKey.PF13 },
        { Key.F12, KeyModifiers.Shift, TerminalKey.PF24 },
        { Key.F1, KeyModifiers.Control, TerminalKey.PF13 },
        { Key.F12, KeyModifiers.Control, TerminalKey.PF24 },
        { Key.PageUp, KeyModifiers.None, TerminalKey.PF7 },
        { Key.PageDown, KeyModifiers.None, TerminalKey.PF8 },
        { Key.Home, KeyModifiers.Control, TerminalKey.PA2 },
        { Key.PageUp, KeyModifiers.Control, TerminalKey.PA3 },
        { Key.D1, KeyModifiers.Alt, TerminalKey.PA1 },
        { Key.D2, KeyModifiers.Alt, TerminalKey.PA2 },
        { Key.D3, KeyModifiers.Alt, TerminalKey.PA3 },
        { Key.Tab, KeyModifiers.None, TerminalKey.Tab },
        { Key.Tab, KeyModifiers.Shift, TerminalKey.BackTab },
        { Key.Insert, KeyModifiers.None, TerminalKey.Insert },
        { Key.Home, KeyModifiers.None, TerminalKey.Home },
        { Key.End, KeyModifiers.None, TerminalKey.EraseEof },
        { Key.Delete, KeyModifiers.None, TerminalKey.Delete },
        { Key.Back, KeyModifiers.None, TerminalKey.Erase },
        { Key.Up, KeyModifiers.None, TerminalKey.Up },
        { Key.Down, KeyModifiers.None, TerminalKey.Down },
        { Key.Left, KeyModifiers.None, TerminalKey.Left },
        { Key.Right, KeyModifiers.None, TerminalKey.Right },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void Vista_defaults(Key key, KeyModifiers modifiers, TerminalKey expected)
    {
        Assert.True(Map.TryMap(new KeyChord(key, modifiers), out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Modifier_taps_send_enter_and_reset()
    {
        Assert.True(Map.TryMap(KeyChord.TapOf(Key.RightCtrl), out var enter));
        Assert.Equal(TerminalKey.Enter, enter);
        Assert.True(Map.TryMap(KeyChord.TapOf(Key.LeftCtrl), out var reset));
        Assert.Equal(TerminalKey.Reset, reset);
        // The press of the modifier itself is not a chord.
        Assert.False(Map.TryMap(new KeyChord(Key.RightCtrl, KeyModifiers.Control), out _));
    }

    [Fact]
    public void Not_and_cent_signs_are_typed()
    {
        Assert.True(Map.TryText(new KeyChord(Key.OemOpenBrackets, KeyModifiers.Control), out var notSign));
        Assert.Equal("¬", notSign);
        Assert.True(Map.TryText(new KeyChord(Key.D6, KeyModifiers.Control), out var cent));
        Assert.Equal("¢", cent);
        Assert.False(Map.TryText(new KeyChord(Key.A), out _));
    }

    [Theory]
    [InlineData(Key.A, KeyModifiers.None)]
    [InlineData(Key.C, KeyModifiers.Control)]
    [InlineData(Key.V, KeyModifiers.Meta)]
    [InlineData(Key.Enter, KeyModifiers.Meta)]
    [InlineData(Key.LeftShift, KeyModifiers.Shift)]
    public void Text_and_platform_shortcut_keys_stay_unmapped(Key key, KeyModifiers modifiers)
    {
        Assert.False(Map.TryMap(new KeyChord(key, modifiers), out _));
        Assert.False(Map.TryText(new KeyChord(key, modifiers), out _));
    }

    [Fact]
    public void Backspace_follows_the_profile_and_the_two_tables_are_cached()
    {
        Assert.True(DefaultKeymap.Create(destructiveBackspace: false).TryMap(new KeyChord(Key.Back), out var cursorLeft));
        Assert.Equal(TerminalKey.Backspace, cursorLeft);
        Assert.Same(DefaultKeymap.Create(true), DefaultKeymap.Create(true));
        Assert.NotSame(DefaultKeymap.Create(true), DefaultKeymap.Create(false));
    }

    [Fact]
    public void With_overrides_entries_and_leaves_the_default_untouched()
    {
        var remapped = Map.With(
            [KeyValuePair.Create(new KeyChord(Key.Escape), TerminalKey.Reset), KeyValuePair.Create(new KeyChord(Key.F13), TerminalKey.Clear)],
            [KeyValuePair.Create(new KeyChord(Key.D6, KeyModifiers.Control), "6")]);

        Assert.True(remapped.TryMap(new KeyChord(Key.Escape), out var escape));
        Assert.Equal(TerminalKey.Reset, escape);
        Assert.True(remapped.TryMap(new KeyChord(Key.F13), out var f13));
        Assert.Equal(TerminalKey.Clear, f13);
        Assert.True(remapped.TryText(new KeyChord(Key.D6, KeyModifiers.Control), out var six));
        Assert.Equal("6", six);
        Assert.Equal(Map.Keys.Count + 1, remapped.Keys.Count);

        Assert.True(Map.TryMap(new KeyChord(Key.Escape), out var original));
        Assert.Equal(TerminalKey.Attn, original);
    }
}
