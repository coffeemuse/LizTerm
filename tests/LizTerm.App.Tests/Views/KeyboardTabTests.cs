// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using LizTerm.App.Controls;
using LizTerm.App.Keyboard;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Views;

/// <summary>The Keyboard tab on its own, in a bare window (PreferencesWindowTests covers it inside Preferences).</summary>
public class KeyboardTabTests : IDisposable
{
    private static readonly KeyGestureFormatInfo Words = new(new Dictionary<Key, string>());

    /// <summary>Only the two note tests write a file, and they write it here: nothing in this class may reach the
    /// user's own keymap.json, so every other test's keymap is the in-memory one.</summary>
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "keymap.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static (Window Window, KeyboardTab Tab, KeymapEditorViewModel Editor, KeymapViewModel Keymap) Show(
        KeymapViewModel? keymap = null)
    {
        keymap ??= new KeymapViewModel();
        var editor = new KeymapEditorViewModel(keymap, () => PlatformHotkeys.Fallback, Words);
        var tab = new KeyboardTab { DataContext = editor };
        var window = new Window { Width = 520, Height = 400, Content = tab };
        window.Show();
        window.UpdateLayout();
        return (window, tab, editor, keymap);
    }

    private static Control RowContainer(KeyboardTab tab, KeymapEditorViewModel editor, string title) =>
        tab.FindControl<ItemsControl>("RowList")!.ContainerFromIndex(editor.Rows.ToList().FindIndex(row => row.Title == title))!;

    /// <summary>A real click: Button runs its Command from OnClick, which a raised ClickEvent never reaches. The
    /// list scrolls, so the control is brought into view first or the press would land on nothing.</summary>
    private static void Click(Window window, Control control)
    {
        control.BringIntoView();
        window.UpdateLayout();
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
    }

    private static ChordCaptureBox SlotOf(Control container) => container.GetVisualDescendants().OfType<ChordCaptureBox>().Single();

    /// <summary>SessionWindowKeymapTests' helper, whose own copy is private to that class: the keypad builds its
    /// buttons on the first show, so the layout has to have run.</summary>
    private static Button KeypadButton(SessionWindow window, TerminalKey key)
    {
        window.UpdateLayout();
        return window.FindControl<Keypad>("KeypadPanel")!.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, key));
    }

    [AvaloniaFact]
    public void There_is_a_row_and_a_slot_for_every_action()
    {
        var (_, tab, editor, _) = Show();

        Assert.Equal(Enum.GetValues<TerminalKey>().Length + 2, tab.FindControl<ItemsControl>("RowList")!.ItemCount);
        Assert.Equal(editor.Rows.Count, tab.GetVisualDescendants().OfType<ChordCaptureBox>().Count());
    }

    [AvaloniaFact]
    public void A_row_shows_its_title_and_its_chips()
    {
        var (_, tab, editor, _) = Show();

        var texts = RowContainer(tab, editor, "PA2").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains("PA2", texts);
        Assert.Contains("Alt+2", texts);
        Assert.Contains("Ctrl+Home", texts);
    }

    [AvaloniaFact]
    public void A_chords_slot_binds_it_to_its_own_row_and_the_chip_appears()
    {
        var (window, tab, editor, keymap) = Show();
        var slot = SlotOf(RowContainer(tab, editor, "PA1"));
        slot.Focus();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.F9, RawInputModifiers.Alt);
        window.UpdateLayout();

        Assert.Equal(new KeymapAction.SendKey(TerminalKey.PA1), keymap.ActionOf(new KeyChord(Key.F9, KeyModifiers.Alt)));
        var texts = RowContainer(tab, editor, "PA1").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Alt+F9", texts);
        Assert.False(slot.IsArmed);
    }

    /// <summary>The way out that binds nothing (#18): Cancel is beside the slot only while it is armed, and a click on
    /// it disarms, leaves the keymap alone and leaves the focus on the slot, so Tab moves on from there.</summary>
    [AvaloniaFact]
    public void A_Cancel_button_beside_an_armed_slot_disarms_it_and_binds_nothing()
    {
        var (window, tab, editor, keymap) = Show();
        var row = RowContainer(tab, editor, "PA1");
        var slot = SlotOf(row);
        var cancel = row.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Cancel"));
        Assert.False(cancel.IsVisible);
        slot.Focus();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.UpdateLayout();
        Assert.True(cancel.IsVisible);
        Assert.Equal("Cancel adding a key to PA1", Avalonia.Automation.AutomationProperties.GetName(cancel));
        var before = keymap.Compose(destructiveBackspace: true);

        Click(window, cancel);

        Assert.False(slot.IsArmed);
        Assert.False(cancel.IsVisible);
        Assert.True(slot.IsFocused);
        Assert.Same(before, keymap.Compose(destructiveBackspace: true));
    }

    [AvaloniaFact]
    public void A_refused_chord_shows_the_reason_in_the_slot()
    {
        var (window, tab, editor, keymap) = Show();
        var slot = SlotOf(RowContainer(tab, editor, "PA1"));
        slot.Focus();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);

        Assert.True(slot.IsArmed);
        Assert.Equal("This would take away typing that character", slot.Text);
        Assert.Null(keymap.ActionOf(new KeyChord(Key.A)));
    }

    [AvaloniaFact]
    public void A_chips_remove_button_unbinds_the_chord()
    {
        var (window, tab, editor, keymap) = Show();
        var remove = RowContainer(tab, editor, "PA2").GetVisualDescendants().OfType<Button>()
            .Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Remove Alt+2");

        Click(window, remove);
        window.UpdateLayout();

        Assert.Null(keymap.ActionOf(new KeyChord(Key.D2, KeyModifiers.Alt)));
        var texts = RowContainer(tab, editor, "PA2").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.DoesNotContain("Alt+2", texts);
    }

    [AvaloniaFact]
    public void Reset_to_defaults_restores_what_was_changed()
    {
        var (window, tab, _, keymap) = Show();
        keymap.Unbind(new KeyChord(Key.D2, KeyModifiers.Alt));
        Assert.Null(keymap.ActionOf(new KeyChord(Key.D2, KeyModifiers.Alt)));

        Click(window, tab.FindControl<Button>("ResetButton")!);

        Assert.Equal(new KeymapAction.SendKey(TerminalKey.PA2), keymap.ActionOf(new KeyChord(Key.D2, KeyModifiers.Alt)));
    }

    [AvaloniaFact]
    public void The_two_notes_show_only_when_there_is_something_to_say()
    {
        var (_, tab, _, _) = Show();

        Assert.False(tab.FindControl<TextBlock>("KeymapSaveErrorText")!.IsVisible);
        Assert.False(tab.FindControl<TextBlock>("UnreadableText")!.IsVisible);
    }

    /// <summary>The shown half of the same pair, through the view: a keymap.json holding an entry this build skipped
    /// puts the note on screen with the editor's words.</summary>
    [AvaloniaFact]
    public void The_unreadable_note_shows_under_the_list_when_the_file_holds_an_entry_this_build_skipped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"bindings": {"Bogus+Home": "PA1"}}""");
        var (_, tab, _, _) = Show(new KeymapViewModel(new KeymapStore(FilePath)));

        var note = tab.FindControl<TextBlock>("UnreadableText")!;

        Assert.True(note.IsVisible);
        Assert.Equal("1 entry in keymap.json could not be read: Bogus+Home (unknown chord). "
                     + "It is kept as written, and Reset to defaults removes it.",
                     note.Text);
    }

    /// <summary>A chord captured in the tab over a file that cannot be written puts the save failure on screen, which
    /// is the whole point of the banner: the change stands in memory and the user is told it did not reach the file.
    /// The file itself reads: a file that does not is the load error below, where there are no rows to capture in.
    /// A directory in the way of the write's temp file is what makes the write fail, TagMaintenance's own trick.</summary>
    [AvaloniaFact]
    public void A_failed_save_shows_its_message_under_the_list()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"bindings": {}}""");
        Directory.CreateDirectory(FilePath + ".tmp");
        var (window, tab, editor, _) = Show(new KeymapViewModel(new KeymapStore(FilePath)));
        var error = tab.FindControl<TextBlock>("KeymapSaveErrorText")!;
        Assert.False(error.IsVisible);
        var slot = SlotOf(RowContainer(tab, editor, "PA1"));
        slot.Focus();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.F9, RawInputModifiers.Alt);
        window.UpdateLayout();

        Assert.True(error.IsVisible);
        Assert.StartsWith("Could not save the keymap", error.Text);
    }

    // ---- a file this build cannot use at all (#168) -------------------------------------------------------------

    /// <summary>The rows go, since every default is in force and nothing would save; the reason, the file and a way
    /// out take their place.</summary>
    [AvaloniaFact]
    public void A_file_that_would_not_load_replaces_the_rows_with_the_reason_and_a_way_out()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "not json");

        var (_, tab, _, _) = Show(new KeymapViewModel(new KeymapStore(FilePath)));

        Assert.False(tab.FindControl<ScrollViewer>("RowScroller")!.IsVisible);
        // Its own IsVisible is untouched; the footer around it is what went, so ask what the user can see.
        Assert.False(tab.FindControl<Button>("ResetButton")!.IsEffectivelyVisible);
        Assert.True(tab.FindControl<StackPanel>("LoadErrorPanel")!.IsVisible);
        Assert.StartsWith("keymap.json could not be read.", tab.FindControl<TextBlock>("LoadErrorText")!.Text);
        Assert.Equal(FilePath, tab.FindControl<SelectableTextBlock>("LoadErrorPath")!.Text);
    }

    /// <summary>And the way out works from the view: the file goes aside under its own name and the rows come back.</summary>
    [AvaloniaFact]
    public void Resetting_from_the_error_panel_brings_the_rows_back()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "not json");
        var (window, tab, _, _) = Show(new KeymapViewModel(new KeymapStore(FilePath)));

        Click(window, tab.FindControl<Button>("RestoreButton")!);
        window.UpdateLayout();

        Assert.False(tab.FindControl<StackPanel>("LoadErrorPanel")!.IsVisible);
        Assert.True(tab.FindControl<ScrollViewer>("RowScroller")!.IsVisible);
        Assert.Equal("not json", File.ReadAllText(FilePath + ".bad"));
    }

    /// <summary>A file that loads keeps its editor, whatever else is wrong with its entries.</summary>
    [AvaloniaFact]
    public void A_file_that_loads_keeps_its_rows()
    {
        var (_, tab, _, _) = Show();

        Assert.True(tab.FindControl<ScrollViewer>("RowScroller")!.IsVisible);
        Assert.False(tab.FindControl<StackPanel>("LoadErrorPanel")!.IsVisible);
    }

    /// <summary>The whole route (#18): a chord captured in the tab inside a real Preferences window reaches an open
    /// session window's screen and its keypad tooltips, because both edit the one process keymap — and Preferences
    /// stays open, since nothing here closes it.</summary>
    [AvaloniaFact]
    public void A_binding_made_in_the_tab_reaches_an_open_session_window_the_screen_and_the_keypad()
    {
        var keymap = new KeymapViewModel();
        var session = new FakeEmulatorSession
        {
            Profile = new SessionProfile { Name = "TSO", Host = "tk5.local", Port = 3270 },
        };
        var sessionWindow = new SessionWindow(MenuStyle.InWindow, isMacOS: false);
        sessionWindow.AttachKeymap(keymap);
        sessionWindow.DataContext = new SessionViewModel(session, action => action(), new FakeTextClipboard());
        sessionWindow.Show();
        // The keypad is off by default and builds its buttons on the first show (keypad spec §2.1), so its tooltips
        // exist only once it is visible.
        ((SessionViewModel)sessionWindow.DataContext).Settings.Keypad = true;

        var preferences = new PreferencesWindow(new SettingsViewModel(), keymap, systemAlertAvailable: true, menuStyleChoosable: true);
        preferences.Show();
        var tabs = preferences.FindControl<TabControl>("Tabs")!;
        // A TabControl hosts only the selected tab's content, so the rows are in the visual tree only once Keyboard
        // is the tab on screen — which is what a user does before touching it anyway.
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(item => Equals(item.Header, "Keyboard"));
        preferences.UpdateLayout();
        var tab = preferences.FindControl<KeyboardTab>("KeyboardPanel")!;
        var editor = (KeymapEditorViewModel)tab.DataContext!;
        var slot = SlotOf(RowContainer(tab, editor, "PA1"));
        slot.Focus();
        preferences.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        preferences.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        preferences.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.Control);

        var screen = sessionWindow.FindControl<TerminalScreen>("Screen")!;
        Assert.True(screen.Keymap.TryMap(new KeyChord(Key.Home, KeyModifiers.Control), out var key));
        Assert.Equal(TerminalKey.PA1, key);
        Assert.Contains("Home", (string)ToolTip.GetTip(KeypadButton(sessionWindow, TerminalKey.PA1))!);
        Assert.True(preferences.IsVisible);
    }
}
