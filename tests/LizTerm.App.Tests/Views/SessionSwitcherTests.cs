// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LizTerm.App.Controls;
using LizTerm.App.Menus;
using LizTerm.App.Sessions;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Views;

/// <summary>The switcher overlay in a real headless SessionWindow (session switching spec §5, §6). The window's
/// own session is TSO; a second, VM370, sits behind a FakeSessionHost and is the previous session.</summary>
public class SessionSwitcherTests
{
    private sealed record Rig(SessionWindow Window, FakeEmulatorSession Session, SessionList List, SessionEntry Own,
        SessionEntry Second, FakeSessionHost SecondHost);

    private static Rig Show(MenuStyle style = MenuStyle.Native)
    {
        var session = new FakeEmulatorSession { Profile = new SessionProfile { Name = "TSO", Host = "tk5.local", Port = 3270 } };
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard { Text = "typed-into-the-host-by-mistake" });
        var window = new SessionWindow(style, isMacOS: true) { DataContext = vm };
        var list = new SessionList();
        var own = new SessionEntry(vm, new ProfileRow(session.Profile, TagRegistry.Empty), true, window);
        var (second, _, secondHost) = TestSessions.Create("VM370", "vm370.local");
        list.Add(own);
        list.Add(second);
        window.AttachSessions(list, own);
        window.Show();
        list.Activated(second);
        list.Activated(own);
        window.FindControl<TerminalScreen>("Screen")!.Focus();
        return new Rig(window, session, list, own, second, secondHost);
    }

    private static SessionSwitcher Panel(SessionWindow window) => window.FindControl<SessionSwitcher>("SwitcherPanel")!;

    private static bool ScreenFocused(SessionWindow window) => window.FindControl<TerminalScreen>("Screen")!.IsFocused;

    private static void OpenWithChord(SessionWindow window) => window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);

    private static Point CentreOf(SessionWindow window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    /// <summary>The only dispatch path under InWindow, and on Windows and Linux: the screen's chord. Over all three
    /// styles, modelled on NativeMenuTests.The_find_gesture_opens_the_bar_via_TerminalScreen.</summary>
    [AvaloniaTheory]
    [InlineData(MenuStyle.Native)]
    [InlineData(MenuStyle.InWindow)]
    [InlineData(MenuStyle.Both)]
    public void The_chord_opens_the_switcher_via_the_screen(MenuStyle style)
    {
        var rig = Show(style);

        OpenWithChord(rig.Window);

        Assert.True(rig.Window.Switcher!.IsOpen);
        Assert.True(Panel(rig.Window).IsVisible);
        Assert.True(Panel(rig.Window).Box.IsFocused);
        Assert.Same(rig.Second, rig.Window.Switcher.Selected!.Entry);
    }

    [AvaloniaFact]
    public void The_chord_again_closes_it_and_returns_focus_to_the_screen()
    {
        var rig = Show();
        OpenWithChord(rig.Window);

        OpenWithChord(rig.Window);

        Assert.False(rig.Window.Switcher!.IsOpen);
        Assert.False(Panel(rig.Window).IsVisible);
        Assert.True(ScreenFocused(rig.Window));
    }

    /// <summary>The highest-consequence rule of the feature, Find's invariant: nothing typed while the switcher is
    /// open reaches the host — letters, a keymap chord (Alt+1 is PA1), Enter with no match, and Escape.</summary>
    [AvaloniaFact]
    public void Nothing_typed_in_the_switcher_reaches_the_host()
    {
        var rig = Show();
        rig.Session.RaiseConnection(ConnectionState.Connected3270);
        OpenWithChord(rig.Window);

        rig.Window.KeyTextInput("zzz");
        rig.Window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Alt);
        rig.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        rig.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.Empty(rig.Session.Calls);
        Assert.Equal(0, rig.SecondHost.BringCount);
        Assert.False(rig.Window.Switcher!.IsOpen);
        Assert.True(ScreenFocused(rig.Window));
    }

    /// <summary>A real keyboard delivers a digit as key-down, then text input, then key-up: this is the sequence
    /// that would leak the digit to the host if the switcher ever took it on key-down instead of TextInput (the
    /// guard OnTunnelTextInput exists to keep, see Task 6's note). Session.Calls stays empty across all three.</summary>
    [AvaloniaFact]
    public void A_digit_with_the_filter_empty_brings_that_session_and_closes()
    {
        var rig = Show();
        rig.Session.RaiseConnection(ConnectionState.Connected3270);
        OpenWithChord(rig.Window);

        rig.Window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.None);
        rig.Window.KeyTextInput("2");
        rig.Window.KeyReleaseQwerty(PhysicalKey.Digit2, RawInputModifiers.None);

        Assert.Equal(1, rig.SecondHost.BringCount);
        Assert.False(rig.Window.Switcher!.IsOpen);
        Assert.Equal("", Panel(rig.Window).Box.Text ?? "");
        Assert.Empty(rig.Session.Calls);
    }

    /// <summary>The digit for this window's own, already-current session (position 1): still a real key-down,
    /// text-input, key-up sequence, and still nothing reaches the host. Choosing the current session brings this
    /// window's own host (a no-op activation) rather than SecondHost, and the switcher's close still refocuses
    /// the screen.</summary>
    [AvaloniaFact]
    public void A_digit_for_this_window_s_own_session_closes_and_focuses_the_screen()
    {
        var rig = Show();
        rig.Session.RaiseConnection(ConnectionState.Connected3270);
        OpenWithChord(rig.Window);

        rig.Window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.None);
        rig.Window.KeyTextInput("1");
        rig.Window.KeyReleaseQwerty(PhysicalKey.Digit1, RawInputModifiers.None);

        Assert.False(rig.Window.Switcher!.IsOpen);
        Assert.True(ScreenFocused(rig.Window));
        Assert.Equal(0, rig.SecondHost.BringCount);
        Assert.Empty(rig.Session.Calls);
    }

    [AvaloniaFact]
    public void A_digit_after_a_letter_goes_into_the_filter()
    {
        var rig = Show();
        OpenWithChord(rig.Window);

        rig.Window.KeyTextInput("v");
        rig.Window.KeyTextInput("3");

        Assert.Equal("v3", rig.Window.Switcher!.Term);
        Assert.Equal(0, rig.SecondHost.BringCount);
        Assert.True(rig.Window.Switcher.IsOpen);
    }

    /// <summary>Enter on the row the switcher preselects (the previous session, spec §5.2): a real key-down and
    /// key-up, and nothing reaches the host either side of the choice.</summary>
    [AvaloniaFact]
    public void Enter_brings_the_previous_session()
    {
        var rig = Show();
        rig.Session.RaiseConnection(ConnectionState.Connected3270);
        OpenWithChord(rig.Window);

        rig.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        rig.Window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal(1, rig.SecondHost.BringCount);
        Assert.False(rig.Window.Switcher!.IsOpen);
        Assert.Empty(rig.Session.Calls);
    }

    /// <summary>A real pointer, not a raised event: the row's Border must have a background to be hit at all (the
    /// Manage Tags swatch lesson, 2026-09-13).</summary>
    [AvaloniaFact]
    public void Clicking_a_row_brings_its_session()
    {
        var rig = Show();
        OpenWithChord(rig.Window);
        rig.Window.UpdateLayout();
        var rows = rig.Window.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "RowBorder").ToList();
        Assert.Equal(2, rows.Count);
        var centre = CentreOf(rig.Window, rows[1]);

        rig.Window.MouseDown(centre, MouseButton.Left);
        rig.Window.MouseUp(centre, MouseButton.Left);

        Assert.Equal(1, rig.SecondHost.BringCount);
        Assert.False(rig.Window.Switcher!.IsOpen);
    }

    [AvaloniaFact]
    public void Clicking_the_dimmed_screen_closes_without_bringing_anything()
    {
        var rig = Show();
        OpenWithChord(rig.Window);
        rig.Window.UpdateLayout();
        var dim = rig.Window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Dim");
        var nearBottom = dim.TranslatePoint(new Point(dim.Bounds.Width / 2, dim.Bounds.Height - 10), rig.Window)!.Value;

        rig.Window.MouseDown(nearBottom, MouseButton.Left);
        rig.Window.MouseUp(nearBottom, MouseButton.Left);

        Assert.False(rig.Window.Switcher!.IsOpen);
        Assert.Equal(0, rig.SecondHost.BringCount);
    }

    /// <summary>The Find box's guard, extended (spec §5.5): on macOS the native Edit items are key equivalents
    /// dispatched ahead of the focused control, so with the switcher's box focused Cmd+V must paste into the box,
    /// never type the clipboard into the host.</summary>
    [AvaloniaFact]
    public async Task The_native_paste_item_pastes_into_the_switcher_box_when_it_is_focused()
    {
        var rig = Show();
        rig.Session.RaiseConnection(ConnectionState.Connected3270);
        OpenWithChord(rig.Window);
        await rig.Window.Clipboard!.SetTextAsync("vm");

        var paste = MenuLookup.Item(NativeMenu.GetMenu(rig.Window), "_Edit", "_Paste")!;
        ((INativeMenuItemExporterEventsImplBridge)paste).RaiseClicked();

        Assert.Equal("vm", Panel(rig.Window).Box.Text);
        Assert.DoesNotContain(rig.Session.Calls, call => call.StartsWith("paste:"));
    }

    /// <summary>Spec §5.2 and §5.5: focus leaving the open switcher closes it. The native Find item is the macOS
    /// Cmd+F key equivalent, dispatched ahead of the focused box; before this, the dimmed switcher stayed up while
    /// Escape in the find box put the keyboard back on the screen and the next letter went to the host.</summary>
    [AvaloniaFact]
    public void Opening_find_from_the_native_menu_closes_the_switcher_and_keeps_the_find_box_focused()
    {
        var rig = Show();
        rig.Session.RaiseConnection(ConnectionState.Connected3270);
        OpenWithChord(rig.Window);

        var find = MenuLookup.Item(NativeMenu.GetMenu(rig.Window), "_Edit", "_Find...")!;
        ((INativeMenuItemExporterEventsImplBridge)find).RaiseClicked();

        Assert.False(rig.Window.Switcher!.IsOpen);
        Assert.False(Panel(rig.Window).IsVisible);
        var findBox = rig.Window.FindControl<TextBox>("FindBox")!;
        Assert.True(findBox.IsFocused);

        rig.Window.KeyTextInput("x");

        Assert.Equal("x", findBox.Text);
        Assert.Empty(rig.Session.Calls);
    }

    /// <summary>The palette has one field, so Tab has nowhere to go. Left to the window, Tab moved focus out of the
    /// box to the menu bar with the switcher still open, and Enter and Escape no longer reached it.</summary>
    [AvaloniaTheory]
    [InlineData(RawInputModifiers.None)]
    [InlineData(RawInputModifiers.Shift)]
    public void Tab_stays_in_the_switcher(RawInputModifiers modifiers)
    {
        var rig = Show();
        rig.Session.RaiseConnection(ConnectionState.Connected3270);
        OpenWithChord(rig.Window);

        rig.Window.KeyPressQwerty(PhysicalKey.Tab, modifiers);

        Assert.True(rig.Window.Switcher!.IsOpen);
        Assert.True(Panel(rig.Window).Box.IsFocused);
        Assert.Empty(rig.Session.Calls);
    }

    /// <summary>The box's own context menu is part of the switcher. Its popup takes focus outside the panel, which is
    /// why the window does not close on the panel's IsKeyboardFocusWithin: right-clicking the filter to paste must
    /// not close the switcher under the menu.</summary>
    [AvaloniaFact]
    public void The_box_context_menu_keeps_the_switcher_open()
    {
        var rig = Show();
        OpenWithChord(rig.Window);

        Panel(rig.Window).Box.RaiseEvent(new ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();

        Assert.IsType<MenuItem>(rig.Window.FocusManager!.GetFocusedElement());
        Assert.True(rig.Window.Switcher!.IsOpen);
        Assert.True(Panel(rig.Window).IsVisible);
    }

    [AvaloniaFact]
    public void A_window_with_no_session_list_ignores_the_chord()
    {
        var window = new SessionWindow(MenuStyle.Native, isMacOS: true)
        {
            DataContext = new SessionViewModel(new FakeEmulatorSession(), action => action(), new FakeTextClipboard()),
        };
        window.Show();
        window.FindControl<TerminalScreen>("Screen")!.Focus();

        OpenWithChord(window);

        Assert.Null(window.Switcher);
        Assert.False(Panel(window).IsVisible);
    }

    [AvaloniaFact]
    public void Closing_the_window_removes_its_session_from_the_list()
    {
        var rig = Show();

        rig.Window.Close();

        Assert.Equal([rig.Second], rig.List.Entries);
    }

    [AvaloniaFact]
    public void Bring_restores_a_minimised_window()
    {
        var rig = Show();
        rig.Window.WindowState = WindowState.Minimized;
        Assert.True(rig.Window.IsMinimized);

        rig.Window.Bring();

        Assert.Equal(WindowState.Normal, rig.Window.WindowState);
        Assert.False(rig.Window.IsMinimized);
    }

    [AvaloniaFact]
    public void Topmost_is_keep_on_top_and_announces_its_changes()
    {
        var rig = Show();
        var changes = 0;
        rig.Window.KeepOnTopChanged += (_, _) => changes++;

        rig.Window.Topmost = true;

        Assert.True(rig.Window.KeepOnTop);
        Assert.Equal(1, changes);
    }
}
