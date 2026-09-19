// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using Avalonia.Input.Platform;
using LizTerm.App.Keyboard;
using LizTerm.App.ViewModels;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>Editable keymap spec §5.3: one row per action over KeymapViewModel's queries, chips that follow every
/// change. Plain [Fact]s: nothing here touches Avalonia's platform, so chips are formatted with an explicit format.</summary>
public class KeymapEditorViewModelTests
{
    private static readonly KeyGestureFormatInfo Words = new(new Dictionary<Key, string>());
    private static readonly KeyChord CtrlHome = new(Key.Home, KeyModifiers.Control);
    private static readonly KeyChord Alt1 = new(Key.D1, KeyModifiers.Alt);
    private static readonly KeyChord Alt2 = new(Key.D2, KeyModifiers.Alt);

    private static (KeymapEditorViewModel Editor, KeymapViewModel Keymap) Build(Func<PlatformHotkeys>? hotkeys = null)
    {
        var keymap = new KeymapViewModel();
        return (new KeymapEditorViewModel(keymap, hotkeys ?? (() => PlatformHotkeys.Fallback), Words), keymap);
    }

    private static KeymapRow Row(KeymapEditorViewModel editor, string title) => editor.Rows.Single(row => row.Title == title);

    private static string[] Texts(KeymapRow row) => [.. row.Chips.Select(chip => chip.Text)];

    [Fact]
    public void Every_3270_key_has_exactly_one_row()
    {
        var (editor, _) = Build();

        var keys = editor.Rows.Select(row => row.Target).OfType<KeymapAction.SendKey>().Select(send => send.Key).ToList();

        Assert.Equal(Enum.GetValues<TerminalKey>().Order(), keys.Order());
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Rows_are_in_the_spec_order_with_the_text_rows_last()
    {
        var (editor, _) = Build();
        var titles = editor.Rows.Select(row => row.Title).ToList();

        Assert.Equal(["Enter", "Newline", "Clear", "Reset", "Attn", "SysReq", "PF1", "PF2"], titles.Take(8));
        Assert.Equal(["PF23", "PF24", "PA1", "PA2", "PA3", "Tab", "Back Tab", "Insert", "Home", "Erase EOF", "Erase Input",
                      "Delete", "Backspace, erasing", "Backspace, moving left", "Dup", "Field Mark", "Up", "Down", "Left", "Right",
                      "Type ¢", "Type ¬"],
                     titles.Skip(titles.IndexOf("PF23")));
    }

    [Fact]
    public void Chips_show_the_defaults_in_tooltip_order()
    {
        var (editor, _) = Build();

        Assert.Equal(["Alt+2", "Ctrl+Home"], Texts(Row(editor, "PA2")));
        Assert.Equal(["F1"], Texts(Row(editor, "PF1")));
        Assert.Equal("a tap of Right Ctrl", Texts(Row(editor, "Enter")).Last());
        Assert.Single(Row(editor, "Backspace, erasing").Chips);
        Assert.Empty(Row(editor, "Backspace, moving left").Chips);
        Assert.Single(Row(editor, "Type ¬").Chips);
    }

    [Fact]
    public void Chips_follow_a_bind_and_an_unbind()
    {
        var (editor, keymap) = Build();

        keymap.Bind(CtrlHome, new KeymapAction.SendKey(TerminalKey.PA1));

        Assert.Equal(["Alt+1", "Ctrl+Home"], Texts(Row(editor, "PA1")));
        Assert.Equal(["Alt+2"], Texts(Row(editor, "PA2")));

        keymap.Unbind(Alt1);

        Assert.Equal(["Ctrl+Home"], Texts(Row(editor, "PA1")));
    }

    [Fact]
    public void A_row_whose_chords_did_not_change_keeps_its_chip_objects()
    {
        var (editor, keymap) = Build();
        var before = Row(editor, "PF1").Chips.Single();

        keymap.Bind(CtrlHome, new KeymapAction.SendKey(TerminalKey.PA1));

        Assert.Same(before, Row(editor, "PF1").Chips.Single());
    }

    [Fact]
    public void A_text_row_stays_after_its_chords_are_removed()
    {
        var (editor, keymap) = Build();

        keymap.Unbind(new KeyChord(Key.OemOpenBrackets, KeyModifiers.Control));

        Assert.Empty(Row(editor, "Type ¬").Chips);
    }

    [Fact]
    public void A_text_binding_the_file_added_gets_its_own_row()
    {
        var (editor, keymap) = Build();

        keymap.Bind(new KeyChord(Key.F9, KeyModifiers.Alt), new KeymapAction.TypeText("§"));

        Assert.Equal(["Alt+F9"], Texts(Row(editor, "Type §")));
        Assert.Equal("Type §", editor.Rows[^1].Title);
    }

    [Fact]
    public void The_two_backspace_rows_carry_the_profile_note_and_no_other_row_does()
    {
        var (editor, _) = Build();

        var withNotes = editor.Rows.Where(row => row.HasNote).Select(row => row.Title).ToList();

        Assert.Equal(["Backspace, erasing", "Backspace, moving left"], withNotes);
        Assert.Contains("profile's Backspace setting", Row(editor, "Backspace, erasing").Note);
    }

    [Fact]
    public void A_row_names_its_slot_for_a_screen_reader()
    {
        var (editor, _) = Build();

        Assert.Equal("Add a key to PA2", Row(editor, "PA2").AddName);
    }

    [Fact]
    public void Disposing_stops_the_rows_following()
    {
        var (editor, keymap) = Build();

        editor.Dispose();
        keymap.Bind(CtrlHome, new KeymapAction.SendKey(TerminalKey.PA1));

        Assert.Equal(["Alt+1"], Texts(Row(editor, "PA1")));
    }
}
