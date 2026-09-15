// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using LizTerm.App.Controls;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Controls;

public class TerminalScreenInputTests
{
    private static (Window Window, TerminalScreen Screen) Show()
    {
        var screen = new TerminalScreen { Snapshot = ScreenSnapshot.Empty(24, 80) };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        screen.Focus();
        return (window, screen);
    }

    [AvaloniaFact]
    public void Function_keys_and_shift_tab_raise_KeyRequested()
    {
        var (window, screen) = Show();
        var keys = new List<TerminalKey>();
        screen.KeyRequested += (_, k) => keys.Add(k);

        window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal([TerminalKey.PF3, TerminalKey.BackTab, TerminalKey.Enter], keys);
    }

    [AvaloniaFact]
    public void Backspace_erases_by_default_and_moves_left_when_the_profile_says_so()
    {
        var (window, screen) = Show();
        var keys = new List<TerminalKey>();
        screen.KeyRequested += (_, k) => keys.Add(k);

        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        screen.DestructiveBackspace = false;
        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);

        Assert.Equal([TerminalKey.Erase, TerminalKey.Backspace], keys);
    }

    /// <summary>#111: Ctrl+I is Insert's second home, for keyboards with no Insert key. Driven through the screen
    /// rather than the table alone, because platform gestures are checked first and one on I would take it.</summary>
    [AvaloniaFact]
    public void Ctrl_I_toggles_insert()
    {
        var (window, screen) = Show();
        var keys = new List<TerminalKey>();
        screen.KeyRequested += (_, k) => keys.Add(k);

        window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Control);

        Assert.Equal([TerminalKey.Insert], keys);
    }

    [AvaloniaFact]
    public void Vista_keys_reach_the_host()
    {
        var (window, screen) = Show();
        var keys = new List<TerminalKey>();
        var text = new List<string>();
        screen.KeyRequested += (_, k) => keys.Add(k);
        screen.TextEntered += (_, t) => text.Add(t);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.Shift);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.PageUp, RawInputModifiers.None);
        // Ctrl+Insert is a Copy gesture in every Avalonia hotkey table, checked before the keymap (spec 6.2), so
        // the table has no PA1 entry for it; Ctrl+Home (PA2) exercises the "Ctrl+key reaches a PA key" path.
        window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Alt);
        window.KeyPressQwerty(PhysicalKey.BracketLeft, RawInputModifiers.Control);

        Assert.Equal([TerminalKey.Attn, TerminalKey.SysReq, TerminalKey.Clear, TerminalKey.PF7, TerminalKey.PA2, TerminalKey.PA1], keys);
        Assert.Equal(["¬"], text);
    }

    [AvaloniaFact]
    public void A_right_ctrl_tap_sends_enter_and_a_ctrl_chord_does_not_reset()
    {
        var (window, screen) = Show();
        var keys = new List<TerminalKey>();
        screen.KeyRequested += (_, k) => keys.Add(k);

        window.KeyPressQwerty(PhysicalKey.ControlRight, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.ControlRight, RawInputModifiers.None);

        window.KeyPressQwerty(PhysicalKey.ControlLeft, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.F1, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);

        window.KeyPressQwerty(PhysicalKey.ControlLeft, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);

        Assert.Equal([TerminalKey.Enter, TerminalKey.PF13, TerminalKey.Reset], keys);
    }

    /// <summary>A Ctrl release is a tap only when nothing at all happened in between: a wheel turn, a click, or a
    /// focus move ends the candidate, so a Ctrl+wheel, a Ctrl+click, or a Ctrl held across a dialog never submits
    /// the screen or resets the keyboard.</summary>
    [AvaloniaFact]
    public void Pointer_input_or_focus_loss_between_a_ctrl_press_and_its_release_is_not_a_tap()
    {
        var screen = new TerminalScreen { Snapshot = ScreenSnapshot.Empty(24, 80), Height = 400 };
        var other = new Button { Content = "other" };
        var panel = new StackPanel();
        panel.Children.Add(screen);
        panel.Children.Add(other);
        var window = new Window { Width = 800, Height = 600, Content = panel };
        window.Show();
        screen.Focus();
        var keys = new List<TerminalKey>();
        screen.KeyRequested += (_, k) => keys.Add(k);
        var inside = new Avalonia.Point(40, 40);

        window.KeyPressQwerty(PhysicalKey.ControlRight, RawInputModifiers.Control);
        window.MouseWheel(inside, new Avalonia.Vector(0, -1), RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.ControlRight, RawInputModifiers.None);

        window.KeyPressQwerty(PhysicalKey.ControlRight, RawInputModifiers.Control);
        window.MouseDown(inside, MouseButton.Left, RawInputModifiers.Control);
        window.MouseUp(inside, MouseButton.Left, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.ControlRight, RawInputModifiers.None);

        window.KeyPressQwerty(PhysicalKey.ControlLeft, RawInputModifiers.Control);
        other.Focus();
        screen.Focus();
        window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);

        Assert.Empty(keys);

        // The detector still works afterwards: a clean tap is still a tap.
        window.KeyPressQwerty(PhysicalKey.ControlRight, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.ControlRight, RawInputModifiers.None);
        Assert.Equal([TerminalKey.Enter], keys);
    }

    [AvaloniaFact]
    public void Tab_stays_on_the_screen_control()
    {
        var screen = new TerminalScreen { Snapshot = ScreenSnapshot.Empty(24, 80) };
        var after = new Button { Content = "after" };
        var panel = new StackPanel();
        panel.Children.Add(screen);
        panel.Children.Add(after);
        var window = new Window { Width = 800, Height = 600, Content = panel };
        window.Show();
        screen.Focus();

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);

        Assert.True(screen.IsFocused);
        Assert.False(after.IsFocused);
    }

    [AvaloniaFact]
    public void Tab_from_button_moves_focus_normally()
    {
        var first = new Button { Content = "first" };
        var second = new Button { Content = "second" };
        var panel = new StackPanel();
        panel.Children.Add(first);
        panel.Children.Add(second);
        var window = new Window { Width = 800, Height = 600, Content = panel };
        window.Show();
        first.Focus();

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);

        Assert.True(second.IsFocused);
    }

    [AvaloniaFact]
    public void Text_input_raises_TextEntered()
    {
        var (window, screen) = Show();
        var texts = new List<string>();
        screen.TextEntered += (_, t) => texts.Add(t);
        window.KeyTextInput("abc");
        Assert.Equal(["abc"], texts);
    }

    [AvaloniaFact]
    public void Click_raises_CellClicked_with_zero_based_cell()
    {
        var (window, screen) = Show();
        (int Row, int Column)? clicked = null;
        screen.CellClicked += (_, c) => clicked = c;
        var g = screen.LastGeometry;
        var rect = g.CellRect(5, 12);
        window.MouseDown(new Avalonia.Point(rect.Center.X, rect.Center.Y), MouseButton.Left);
        window.MouseUp(new Avalonia.Point(rect.Center.X, rect.Center.Y), MouseButton.Left);
        Assert.Equal((5, 12), clicked);
    }

    /// <summary>The session switcher's chord (session switching spec §6), beside Find's: the platform's command
    /// modifier plus K. It is checked before the keymap, so it can never become a key or text for the host. The
    /// headless platform's CommandModifiers is Control, as the Find chord's tests rely on.</summary>
    [AvaloniaFact]
    public void The_command_modifier_with_k_raises_SwitcherRequested_and_sends_nothing()
    {
        var (window, screen) = Show();
        var requests = 0;
        var keys = new List<TerminalKey>();
        var text = new List<string>();
        screen.SwitcherRequested += (_, _) => requests++;
        screen.KeyRequested += (_, key) => keys.Add(key);
        screen.TextEntered += (_, typed) => text.Add(typed);

        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);

        Assert.Equal(1, requests);
        Assert.Empty(keys);
        Assert.Empty(text);
    }
}
