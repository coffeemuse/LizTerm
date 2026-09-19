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
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Views;

/// <summary>The Keyboard tab on its own, in a bare window (PreferencesWindowTests covers it inside Preferences).</summary>
public class KeyboardTabTests
{
    private static readonly KeyGestureFormatInfo Words = new(new Dictionary<Key, string>());

    private static (Window Window, KeyboardTab Tab, KeymapEditorViewModel Editor, KeymapViewModel Keymap) Show()
    {
        var keymap = new KeymapViewModel();
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

        Click(window, tab.FindControl<Button>("ResetButton")!);

        Assert.Equal(new KeymapAction.SendKey(TerminalKey.PA2), keymap.ActionOf(new KeyChord(Key.D2, KeyModifiers.Alt)));
    }

    [AvaloniaFact]
    public void The_two_notes_show_only_when_there_is_something_to_say()
    {
        var (_, tab, _, _) = Show();

        Assert.False(tab.FindControl<TextBlock>("SaveErrorText")!.IsVisible);
        Assert.False(tab.FindControl<TextBlock>("UnreadableText")!.IsVisible);
    }
}
