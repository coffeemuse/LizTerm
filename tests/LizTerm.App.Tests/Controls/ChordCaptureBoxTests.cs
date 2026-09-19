// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using LizTerm.App.Controls;
using LizTerm.App.Keyboard;

namespace LizTerm.App.Tests.Controls;

/// <summary>The Add slot (editable keymap spec §5.3, refined: armed by activating it, not by focusing it).</summary>
public class ChordCaptureBoxTests
{
    private static readonly CaptureResult Accept = new(true, null);
    private static readonly CaptureResult Refuse = new(false, "This would take away typing that character");

    private static (Window Window, ChordCaptureBox Box, Button Other, Button After, List<KeyChord> Offered) Show(
        CaptureResult? answer = null)
    {
        var offered = new List<KeyChord>();
        var box = new ChordCaptureBox { CaptureHandler = chord => { offered.Add(chord); return answer ?? Accept; } };
        var other = new Button { Content = "Other" };
        // A stop after the slot, so "Tab moves on" has somewhere to go rather than wrapping or staying.
        var after = new Button { Content = "After" };
        var window = new Window { Content = new StackPanel { Children = { other, box, after } } };
        window.Show();
        box.Focus();
        return (window, box, other, after, offered);
    }

    private static void Arm(Window window)
    {
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
    }

    private static Point Centre(Window window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    [AvaloniaFact]
    public void An_idle_slot_reads_Add_and_offers_nothing()
    {
        var (window, box, _, _, offered) = Show();

        window.KeyPressQwerty(PhysicalKey.F9, RawInputModifiers.None);

        Assert.Equal("Add", box.Text);
        Assert.False(box.IsArmed);
        Assert.Empty(offered);
    }

    [AvaloniaFact]
    public void Enter_arms_a_focused_slot_and_is_not_itself_a_chord()
    {
        var (window, box, _, _, offered) = Show();

        Arm(window);

        Assert.True(box.IsArmed);
        Assert.Equal("Press a key", box.Text);
        Assert.Empty(offered);
    }

    [AvaloniaFact]
    public void A_chord_is_offered_with_its_modifiers_and_an_accepted_one_disarms()
    {
        var (window, box, _, _, offered) = Show();
        Arm(window);

        window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.Control);

        Assert.Equal([new KeyChord(Key.Home, KeyModifiers.Control)], offered);
        Assert.False(box.IsArmed);
        Assert.Equal("Add", box.Text);
    }

    [AvaloniaFact]
    public void A_refusal_shows_its_reason_and_stays_armed()
    {
        var (window, box, _, _, offered) = Show(Refuse);
        Arm(window);

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);

        Assert.True(box.IsArmed);
        Assert.Equal("This would take away typing that character", box.Text);
        Assert.Single(offered);
    }

    [AvaloniaFact]
    public async Task An_accepted_note_shows_for_a_moment_and_then_goes()
    {
        var (window, box, _, _, _) = Show(new CaptureResult(true, "Moved from PA2"));
        box.MessageDuration = TimeSpan.FromMilliseconds(30);
        Arm(window);

        window.KeyPressQwerty(PhysicalKey.F9, RawInputModifiers.Alt);

        Assert.False(box.IsArmed);
        Assert.Equal("Moved from PA2", box.Text);
        for (var i = 0; i < 100 && box.Text != "Add"; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal("Add", box.Text);
    }

    [AvaloniaFact]
    public void A_Ctrl_key_pressed_and_released_alone_is_offered_as_a_tap()
    {
        var (window, box, _, _, offered) = Show();
        Arm(window);

        window.KeyPressQwerty(PhysicalKey.ControlLeft, RawInputModifiers.Control);
        Assert.Empty(offered);
        window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);

        Assert.Equal([KeyChord.TapOf(Key.LeftCtrl)], offered);
        Assert.False(box.IsArmed);
    }

    [AvaloniaFact]
    public void Ctrl_then_C_is_the_chord_and_its_releases_are_not_a_tap()
    {
        var (window, box, _, _, offered) = Show(new CaptureResult(false, "LizTerm uses this for Copy"));
        Arm(window);

        window.KeyPressQwerty(PhysicalKey.ControlLeft, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.C, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);

        Assert.Equal([new KeyChord(Key.C, KeyModifiers.Control)], offered);
        Assert.True(box.IsArmed);
    }

    [AvaloniaFact]
    public void Shift_pressed_alone_is_waited_on_not_offered()
    {
        var (window, box, _, _, offered) = Show();
        Arm(window);

        window.KeyPressQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.Shift);
        window.KeyReleaseQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.None);

        Assert.Empty(offered);
        Assert.True(box.IsArmed);
    }

    [AvaloniaFact]
    public void Escape_Tab_and_Enter_are_capturable_and_neither_move_focus_nor_close_the_window()
    {
        var (window, box, _, _, offered) = Show(Refuse);
        var done = new Button { Content = "Done", IsDefault = true, IsCancel = true };
        var doneClicks = 0;
        done.Click += (_, _) => doneClicks++;
        ((StackPanel)window.Content!).Children.Add(done);
        Arm(window);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal(
            [new KeyChord(Key.Escape), new KeyChord(Key.Tab), new KeyChord(Key.Tab, KeyModifiers.Shift), new KeyChord(Key.Enter)],
            offered);
        Assert.True(box.IsFocused);
        Assert.True(window.IsVisible);
        Assert.Equal(0, doneClicks);
    }

    [AvaloniaFact]
    public void Space_that_binds_does_not_reopen_the_slot_on_its_release()
    {
        var (window, box, _, _, offered) = Show();
        Arm(window);

        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

        Assert.Equal([new KeyChord(Key.Space)], offered);
        Assert.False(box.IsArmed);
    }

    [AvaloniaFact]
    public void Losing_focus_disarms()
    {
        var (window, box, other, _, _) = Show();
        Arm(window);

        other.Focus();

        Assert.False(box.IsArmed);
        Assert.Equal("Add", box.Text);
    }

    /// <summary>Button activates from the Enter press, so the press that arms the slot is still down when it becomes
    /// armed: the OS auto-repeat's next press, now armed, would offer the chord Enter and bind the 3270 Enter key to
    /// whatever row the user was only opening ("Moved from Enter"), then re-arm on the next repeat and do it
    /// again.</summary>
    [AvaloniaFact]
    public void A_held_Enter_that_armed_the_slot_is_not_captured_by_its_own_auto_repeat()
    {
        var (window, box, _, _, offered) = Show();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.True(box.IsArmed);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Empty(offered);
        Assert.True(box.IsArmed);
        Assert.Equal("Press a key", box.Text);

        // Released and pressed again, Enter is a chord like any other.
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal([new KeyChord(Key.Enter)], offered);
    }

    /// <summary>Disarming owes no swallow: a refusal leaves the refused key consumed, and a click elsewhere before its
    /// release would otherwise make the slot eat the next Space that tried to arm it.</summary>
    [AvaloniaFact]
    public void Disarming_before_a_refused_keys_release_still_leaves_Space_able_to_arm_it()
    {
        var (window, box, other, _, _) = Show(Refuse);
        Arm(window);

        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Assert.True(box.IsArmed);
        other.Focus();
        Assert.False(box.IsArmed);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

        box.Focus();
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

        Assert.True(box.IsArmed);
    }

    [AvaloniaFact]
    public void An_idle_slot_lets_Tab_move_focus_on()
    {
        var (window, box, _, after, offered) = Show();

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);

        Assert.False(box.IsFocused);
        Assert.True(after.IsFocused);
        Assert.Empty(offered);
    }

    [AvaloniaFact]
    public void A_click_arms_and_a_second_click_disarms()
    {
        var (window, box, other, _, _) = Show();
        other.Focus();
        window.UpdateLayout();
        var point = Centre(window, box);

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Assert.True(box.IsArmed);
        Assert.True(box.IsFocused);

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Assert.False(box.IsArmed);
    }

    [AvaloniaFact]
    public void The_slot_takes_the_button_theme_and_marks_the_armed_state_with_a_pseudo_class()
    {
        var (window, box, _, _, _) = Show();
        window.UpdateLayout();

        Assert.True(box.Bounds.Height > 20, "a Button subclass without StyleKeyOverride has no template");
        Assert.DoesNotContain(":armed", box.Classes);
        Arm(window);
        Assert.Contains(":armed", box.Classes);
    }
}
