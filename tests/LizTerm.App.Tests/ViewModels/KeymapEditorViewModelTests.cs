// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using Avalonia.Input.Platform;
using LizTerm.App.Keyboard;
using LizTerm.App.ViewModels;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>Editable keymap spec §5.3: one row per action over KeymapViewModel's queries, chips that follow every
/// change. Plain [Fact]s: nothing here touches Avalonia's platform, so chips are formatted with an explicit format.</summary>
public class KeymapEditorViewModelTests : IDisposable
{
    private static readonly KeyGestureFormatInfo Words = new(new Dictionary<Key, string>());
    private static readonly KeyChord CtrlHome = new(Key.Home, KeyModifiers.Control);
    private static readonly KeyChord Alt1 = new(Key.D1, KeyModifiers.Alt);
    private static readonly KeyChord Alt2 = new(Key.D2, KeyModifiers.Alt);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "keymap.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

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
        Assert.Equal(["PF23", "PF24", "PA1", "PA2", "PA3", "Tab", "Back Tab", "Insert", "Home", "Field End", "Erase EOF", "Erase Input",
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

    // ---- capture --------------------------------------------------------------------------------------------

    [Fact]
    public void Capturing_a_free_chord_binds_it_and_says_nothing()
    {
        var (editor, keymap) = Build();
        var row = Row(editor, "PA1");

        var result = row.TryCapture(new KeyChord(Key.F9, KeyModifiers.Alt));

        Assert.Equal(new CaptureResult(true, null), result);
        Assert.Equal(new KeymapAction.SendKey(TerminalKey.PA1), keymap.ActionOf(new KeyChord(Key.F9, KeyModifiers.Alt)));
        Assert.Equal(["Alt+F9", "Alt+1"], Texts(row));
    }

    [Fact]
    public void Capturing_a_chord_another_row_holds_moves_it_and_names_the_row()
    {
        var (editor, keymap) = Build();

        var result = Row(editor, "PA1").TryCapture(Alt2);

        Assert.Equal(new CaptureResult(true, "Moved from PA2"), result);
        Assert.Equal(new KeymapAction.SendKey(TerminalKey.PA1), keymap.ActionOf(Alt2));
        Assert.Equal(["Ctrl+Home"], Texts(Row(editor, "PA2")));
    }

    [Fact]
    public void Capturing_a_chord_that_types_text_names_the_text_row()
    {
        var (editor, _) = Build();

        var result = Row(editor, "PA1").TryCapture(new KeyChord(Key.OemOpenBrackets, KeyModifiers.Control));

        Assert.Equal(new CaptureResult(true, "Moved from Type ¬"), result);
    }

    [Fact]
    public void Capturing_a_chord_onto_the_text_row_types_that_text()
    {
        var (editor, keymap) = Build();
        var chord = new KeyChord(Key.F9, KeyModifiers.Alt);

        Row(editor, "Type ¬").TryCapture(chord);

        Assert.Equal(new KeymapAction.TypeText("¬"), keymap.ActionOf(chord));
    }

    [Fact]
    public void Capturing_the_chord_a_row_already_has_writes_nothing()
    {
        var (editor, keymap) = Build();
        var changes = 0;
        keymap.Changed += (_, _) => changes++;

        var result = Row(editor, "PA2").TryCapture(Alt2);

        Assert.Equal(new CaptureResult(true, "Already does this"), result);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Capturing_Backspace_onto_the_erasing_row_is_written_though_it_is_already_the_default()
    {
        var (editor, keymap) = Build();
        var backspace = new KeyChord(Key.Back);

        var result = Row(editor, "Backspace, erasing").TryCapture(backspace);

        Assert.Equal(new CaptureResult(true, null), result);
        Assert.True(keymap.Overlay.Entries.ContainsKey(backspace));
    }

    /// <summary>Which Backspace action the unmodified key did was the profile's choice, not the erasing default the
    /// rows answer from, so a Backspace taken from one Backspace row onto the other is bound without a "Moved from".</summary>
    [Fact]
    public void Capturing_Backspace_onto_the_moving_left_row_binds_it_without_claiming_a_move()
    {
        var (editor, keymap) = Build();
        var backspace = new KeyChord(Key.Back);

        var result = Row(editor, "Backspace, moving left").TryCapture(backspace);

        Assert.Equal(new CaptureResult(true, null), result);
        Assert.Equal(new KeymapAction.SendKey(TerminalKey.Backspace), keymap.ActionOf(backspace));
        Assert.Equal(["Backspace"], Texts(Row(editor, "Backspace, moving left")));
        Assert.Empty(Row(editor, "Backspace, erasing").Chips);
    }

    /// <summary>Every row asks the keymap on every change, so the composition is one instance per change, not one
    /// per row.</summary>
    [Fact]
    public void The_rows_share_one_composition_per_change()
    {
        var (_, keymap) = Build();
        var before = keymap.Compose(destructiveBackspace: true);

        Assert.Same(before, keymap.Compose(destructiveBackspace: true));
        keymap.Bind(CtrlHome, new KeymapAction.SendKey(TerminalKey.PA1));

        var after = keymap.Compose(destructiveBackspace: true);
        Assert.NotSame(before, after);
        Assert.Same(after, keymap.Compose(destructiveBackspace: true));
        Assert.True(after.TryMap(CtrlHome, out var key) && key == TerminalKey.PA1);
    }

    [Fact]
    public void A_refused_chord_binds_nothing_and_gives_the_policys_reason()
    {
        var (editor, keymap) = Build();
        var changes = 0;
        keymap.Changed += (_, _) => changes++;

        var result = Row(editor, "PA1").TryCapture(new KeyChord(Key.C, KeyModifiers.Control));

        Assert.Equal(new CaptureResult(false, "LizTerm uses this for Copy"), result);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void The_refusal_uses_the_platform_the_editor_was_given()
    {
        var (editor, _) = Build(() => PlatformHotkeys.MacOS);

        var result = Row(editor, "PA1").TryCapture(new KeyChord(Key.OemComma, KeyModifiers.Meta));

        Assert.Equal(new CaptureResult(false, "The menu bar sees Cmd shortcuts before the screen does"), result);
    }

    [Fact]
    public void A_tap_is_allowed_and_moves_from_the_row_that_had_it()
    {
        var (editor, keymap) = Build();

        var result = Row(editor, "Enter").TryCapture(KeyChord.TapOf(Key.LeftCtrl));

        Assert.Equal(new CaptureResult(true, "Moved from Reset"), result);
        Assert.Equal(new KeymapAction.SendKey(TerminalKey.Enter), keymap.ActionOf(KeyChord.TapOf(Key.LeftCtrl)));
    }

    [Fact]
    public void The_capture_handler_is_the_capture()
    {
        var (editor, _) = Build();

        var result = Row(editor, "PA1").CaptureHandler(new KeyChord(Key.F9, KeyModifiers.Alt));

        Assert.True(result.Accepted);
    }

    // ---- remove, reset ----------------------------------------------------------------------------------------

    [Fact]
    public void A_chips_remove_command_unbinds_its_chord()
    {
        var (editor, keymap) = Build();

        Row(editor, "PA2").Chips.Single(chip => chip.Text == "Alt+2").RemoveCommand.Execute(null);

        Assert.Null(keymap.ActionOf(Alt2));
        Assert.Equal(["Ctrl+Home"], Texts(Row(editor, "PA2")));
    }

    [Fact]
    public void Reset_to_defaults_brings_every_default_back()
    {
        var (editor, keymap) = Build();
        keymap.Bind(CtrlHome, new KeymapAction.SendKey(TerminalKey.PA1));
        keymap.Unbind(Alt2);

        editor.ResetCommand.Execute(null);

        Assert.Equal(["Alt+2", "Ctrl+Home"], Texts(Row(editor, "PA2")));
        Assert.Equal(["Alt+1"], Texts(Row(editor, "PA1")));
    }

    // ---- the notes under the list -----------------------------------------------------------------------------

    [Fact]
    public void The_unreadable_note_appears_only_when_the_file_has_entries_this_build_skipped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"bindings": {"Bogus+Home": "PA1", "Ctrl+F9": "NoSuchKey"}}""");
        var keymap = new KeymapViewModel(new KeymapStore(FilePath));
        var editor = new KeymapEditorViewModel(keymap, () => PlatformHotkeys.Fallback, Words);
        var raised = new List<string?>();
        editor.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.True(editor.HasUnreadable);
        Assert.Equal("2 entries in keymap.json could not be read: Bogus+Home (unknown chord), Ctrl+F9 (unknown key \"NoSuchKey\"). "
                     + "They are kept as written, and Reset to defaults removes them.",
                     editor.UnreadableNote);

        editor.ResetCommand.Execute(null);

        Assert.False(editor.HasUnreadable);
        Assert.Contains(nameof(KeymapEditorViewModel.HasUnreadable), raised);
        Assert.Contains(nameof(KeymapEditorViewModel.UnreadableNote), raised);
    }

    [Fact]
    public void A_file_that_would_not_load_at_all_replaces_the_rows_with_its_reason()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "not json");
        var editor = new KeymapEditorViewModel(new KeymapViewModel(new KeymapStore(FilePath)), () => PlatformHotkeys.Fallback, Words);

        Assert.True(editor.HasLoadError);
        Assert.Equal("keymap.json could not be read. It is not valid JSON. Line 1: 'not json' is an invalid JSON literal. "
                     + "Expected the literal 'null'. LizTerm is using the default keys until it is fixed; your file has not been changed.",
                     editor.LoadErrorNote);
        Assert.Equal(FilePath, editor.KeymapFilePath);
        // The skipped-entry note is for a file that loaded; this one has nothing to list.
        Assert.False(editor.HasUnreadable);
    }

    [Fact]
    public void Restoring_the_defaults_puts_the_editor_back()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "not json");
        var editor = new KeymapEditorViewModel(new KeymapViewModel(new KeymapStore(FilePath)), () => PlatformHotkeys.Fallback, Words);
        var raised = new List<string?>();
        editor.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        editor.RestoreCommand.Execute(null);

        Assert.False(editor.HasLoadError);
        Assert.Null(editor.LoadErrorNote);
        Assert.Contains(nameof(KeymapEditorViewModel.HasLoadError), raised);
        Assert.Contains(nameof(KeymapEditorViewModel.LoadErrorNote), raised);
        Assert.Equal("not json", File.ReadAllText(FilePath + ".bad"));
    }

    [Fact]
    public void A_file_that_loads_has_no_load_error()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"bindings": {"Ctrl+Home": "PA1"}}""");
        var editor = new KeymapEditorViewModel(new KeymapViewModel(new KeymapStore(FilePath)), () => PlatformHotkeys.Fallback, Words);

        Assert.False(editor.HasLoadError);
        Assert.Null(editor.LoadErrorNote);
    }

    [Fact]
    public void One_unreadable_entry_is_worded_in_the_singular()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"bindings": {"Bogus+Home": "PA1"}}""");
        var editor = new KeymapEditorViewModel(new KeymapViewModel(new KeymapStore(FilePath)), () => PlatformHotkeys.Fallback, Words);

        Assert.Equal("1 entry in keymap.json could not be read: Bogus+Home (unknown chord). "
                     + "It is kept as written, and Reset to defaults removes it.",
                     editor.UnreadableNote);
    }

    [Fact]
    public void Every_kind_of_skipped_entry_says_what_is_wrong_with_it()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"bindings": {"Bogus+Home": "PA1", "F1": "NoSuchKey", "F2": 7, "F3": {"text": ""}}}""");
        var editor = new KeymapEditorViewModel(new KeymapViewModel(new KeymapStore(FilePath)), () => PlatformHotkeys.Fallback, Words);

        Assert.Equal("4 entries in keymap.json could not be read: Bogus+Home (unknown chord), F1 (unknown key \"NoSuchKey\"), "
                     + "F2 (not a key name or text), F3 (no text to type). "
                     + "They are kept as written, and Reset to defaults removes them.",
                     editor.UnreadableNote);
    }

    [Fact]
    public void A_long_list_of_skipped_entries_is_cut_short()
    {
        Directory.CreateDirectory(_dir);
        var entries = string.Join(", ", Enumerable.Range(1, 7).Select(n => $"\"Bogus{n}+Home\": \"PA1\""));
        File.WriteAllText(FilePath, "{\"bindings\": {" + entries + "}}");
        var editor = new KeymapEditorViewModel(new KeymapViewModel(new KeymapStore(FilePath)), () => PlatformHotkeys.Fallback, Words);

        Assert.Equal("7 entries in keymap.json could not be read: Bogus1+Home (unknown chord), Bogus2+Home (unknown chord), "
                     + "Bogus3+Home (unknown chord), Bogus4+Home (unknown chord), Bogus5+Home (unknown chord), and 2 more. "
                     + "They are kept as written, and Reset to defaults removes them.",
                     editor.UnreadableNote);
    }

    [Fact]
    public void A_failed_save_reaches_the_tabs_banner_and_the_change_still_stands()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "not json");
        var keymap = new KeymapViewModel(new KeymapStore(FilePath));
        var editor = new KeymapEditorViewModel(keymap, () => PlatformHotkeys.Fallback, Words);
        var raised = new List<string?>();
        editor.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.False(editor.HasSaveError);

        Row(editor, "PA1").TryCapture(new KeyChord(Key.F9, KeyModifiers.Alt));

        Assert.True(editor.HasSaveError);
        Assert.StartsWith("Could not save the keymap", editor.SaveError);
        Assert.Contains(nameof(KeymapEditorViewModel.SaveError), raised);
        Assert.Contains(nameof(KeymapEditorViewModel.HasSaveError), raised);
        Assert.Equal(["Alt+F9", "Alt+1"], Texts(Row(editor, "PA1")));
    }

    /// <summary>Dispose drops the PropertyChanged subscription as well as Changed: the keymap outlives the tab, so a
    /// failed save after the window closed must announce nothing to a disposed editor.</summary>
    [Fact]
    public void A_disposed_editor_is_told_nothing_by_a_later_failed_save()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "not json");
        var keymap = new KeymapViewModel(new KeymapStore(FilePath));
        var editor = new KeymapEditorViewModel(keymap, () => PlatformHotkeys.Fallback, Words);
        editor.Dispose();
        var raised = new List<string?>();
        editor.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        keymap.Bind(new KeyChord(Key.F9, KeyModifiers.Alt), new KeymapAction.SendKey(TerminalKey.PA1));

        Assert.StartsWith("Could not save the keymap", keymap.LastSaveError);
        Assert.Empty(raised);
    }
}
