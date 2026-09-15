// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LizTerm.App.Menus;
using LizTerm.App.Sessions;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Views;

/// <summary>The Window menu (session switching spec §7) in a real headless window: TSO is this window, CONSOLE and
/// IMON sit behind fake hosts. Native style on macOS unless a test names another shape, because only there are the
/// native items attached to the window's menu.</summary>
public class WindowMenuTests
{
    private sealed record Rig(SessionWindow Window, SessionList List, SessionEntry Own,
        SessionEntry Console, FakeEmulatorSession ConsoleSession, FakeSessionHost ConsoleHost,
        SessionEntry Imon, FakeSessionHost ImonHost);

    private static Rig Show(MenuStyle style = MenuStyle.Native, bool isMacOS = true)
    {
        var session = new FakeEmulatorSession
        {
            Profile = new SessionProfile { Name = "TSO", Host = "tk5.local", Port = 3270 },
            ConnectionState = ConnectionState.Connected3270,
        };
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard());
        var window = new SessionWindow(style, isMacOS) { DataContext = vm };
        var (list, own, others) = TestSessions.Attach(window, vm, "CONSOLE", "IMON");
        window.Show();
        return new Rig(window, list, own, others[0].Entry, others[0].Session, others[0].Host, others[1].Entry, others[1].Host);
    }

    private static NativeMenu WindowMenu(SessionWindow window) =>
        MenuLookup.Item(NativeMenu.GetMenu(window), "_Window")?.Menu
        ?? throw new InvalidOperationException("no native Window menu");

    private static NativeMenuItem Native(SessionWindow window, string header) =>
        MenuLookup.Item(WindowMenu(window), header) ?? throw new InvalidOperationException($"no native _Window > {header}");

    /// <summary>The generated rows: everything after the menu's last separator.</summary>
    private static List<NativeMenuItem> NativeRows(SessionWindow window)
    {
        var items = WindowMenu(window).Items.ToList();
        var last = items.FindLastIndex(item => item is NativeMenuItemSeparator);
        return [.. items.Skip(last + 1).OfType<NativeMenuItem>()];
    }

    private static List<MenuItem> ClassicRows(SessionWindow window)
    {
        var menu = window.FindControl<MenuItem>("WindowMenuItem")!;
        var separator = window.FindControl<Separator>("SessionsSeparator")!;
        return [.. menu.Items.Cast<object>().SkipWhile(item => !ReferenceEquals(item, separator)).Skip(1).OfType<MenuItem>()];
    }

    private static void Click(NativeMenuItem item) => ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();

    [AvaloniaFact]
    public void The_window_menu_declares_its_fixed_items_in_order()
    {
        var rig = Show();

        var fixedHeaders = WindowMenu(rig.Window).Items
            .TakeWhile(item => !ReferenceEquals(item, WindowMenu(rig.Window).Items.OfType<NativeMenuItemSeparator>().Last()))
            .OfType<NativeMenuItem>().Where(item => item is not NativeMenuItemSeparator).Select(item => item.Header);

        Assert.Equal(["_Minimize", "_Zoom", "_Keep on Top", "_Switch Session...", "_Bring All to Front"], fixedHeaders);
    }

    [AvaloniaFact]
    public void Each_open_session_gets_a_numbered_row_in_opening_order_in_both_menus()
    {
        var rig = Show();
        string[] expected = ["_1  TSO - tk5.local", "_2  CONSOLE - host.local", "_3  IMON - host.local"];

        Assert.Equal(expected, NativeRows(rig.Window).Select(item => item.Header));
        Assert.Equal(expected, ClassicRows(rig.Window).Select(item => item.Header as string));
    }

    [AvaloniaFact]
    public void The_check_mark_follows_the_current_session()
    {
        var rig = Show();

        rig.List.Activated(rig.Imon);

        Assert.Equal([false, false, true], NativeRows(rig.Window).Select(item => item.IsChecked));
        Assert.Equal([false, false, true], ClassicRows(rig.Window).Select(item => item.IsChecked));
    }

    [AvaloniaFact]
    public void Clicking_a_row_brings_its_session()
    {
        var rig = Show();

        Click(NativeRows(rig.Window)[1]);

        Assert.Equal(1, rig.ConsoleHost.BringCount);
    }

    /// <summary>The in-window renderer toggles a check box's mark before the click arrives, and bringing the window
    /// that is already current changes nothing that would rebuild the rows — so the handler puts the marks back.
    /// A raised ClickEvent skips the toggle, so the test does the toggle itself first.</summary>
    [AvaloniaFact]
    public void Clicking_the_current_row_leaves_its_mark_on()
    {
        var rig = Show();
        rig.List.Activated(rig.Own);
        var row = ClassicRows(rig.Window)[0];
        row.IsChecked = false;

        row.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.True(ClassicRows(rig.Window)[0].IsChecked);
    }

    [AvaloniaFact]
    public void The_rows_rebuild_when_a_session_disconnects_or_closes()
    {
        var rig = Show();

        rig.ConsoleSession.RaiseConnection(ConnectionState.Disconnected);
        Assert.Equal("_2  CONSOLE - host.local (Disconnected)", NativeRows(rig.Window)[1].Header);

        rig.List.Remove(rig.Console);
        Assert.Equal(["_1  TSO - tk5.local", "_2  IMON - host.local"], NativeRows(rig.Window).Select(item => item.Header));
        Assert.Equal(["_1  TSO - tk5.local", "_2  IMON - host.local"], ClassicRows(rig.Window).Select(item => item.Header as string));
    }

    [AvaloniaFact]
    public void A_window_with_no_session_list_hides_the_sessions_separator()
    {
        var window = new SessionWindow(MenuStyle.Native, isMacOS: true)
        {
            DataContext = new SessionViewModel(new FakeEmulatorSession(), action => action(), new FakeTextClipboard()),
        };
        window.Show();

        Assert.Empty(NativeRows(window));
        Assert.False(WindowMenu(window).Items.OfType<NativeMenuItemSeparator>().Last().IsVisible);
        Assert.False(window.FindControl<Separator>("SessionsSeparator")!.IsVisible);
    }

    [AvaloniaFact]
    public void Keep_on_top_toggles_topmost_and_both_marks()
    {
        var rig = Show();
        var classic = rig.Window.FindControl<MenuItem>("KeepOnTopMenuItem")!;

        Click(Native(rig.Window, "_Keep on Top"));
        Assert.True(rig.Window.Topmost);
        Assert.True(Native(rig.Window, "_Keep on Top").IsChecked);
        Assert.True(classic.IsChecked);

        classic.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.False(rig.Window.Topmost);
        Assert.False(Native(rig.Window, "_Keep on Top").IsChecked);
        Assert.False(classic.IsChecked);
    }

    [AvaloniaFact]
    public void Bring_all_to_front_brings_every_other_session()
    {
        var rig = Show();

        Click(Native(rig.Window, "_Bring All to Front"));

        Assert.Equal(1, rig.ConsoleHost.BringCount);
        Assert.Equal(1, rig.ImonHost.BringCount);
    }

    [AvaloniaFact]
    public void Switch_session_opens_the_switcher()
    {
        var rig = Show();

        Click(Native(rig.Window, "_Switch Session..."));

        Assert.True(rig.Window.Switcher!.IsOpen);
    }

    [AvaloniaFact]
    public void Minimize_and_zoom_change_the_window_state()
    {
        var rig = Show();

        Click(Native(rig.Window, "_Minimize"));
        Assert.Equal(WindowState.Minimized, rig.Window.WindowState);

        rig.Window.Bring();
        Click(Native(rig.Window, "_Zoom"));
        Assert.Equal(WindowState.Maximized, rig.Window.WindowState);

        Click(Native(rig.Window, "_Zoom"));
        Assert.Equal(WindowState.Normal, rig.Window.WindowState);
    }

    [AvaloniaFact]
    public void Bring_restores_a_maximised_window_as_maximised()
    {
        var rig = Show();
        rig.Window.WindowState = WindowState.Maximized;
        rig.Window.WindowState = WindowState.Minimized;

        rig.Window.Bring();

        Assert.Equal(WindowState.Maximized, rig.Window.WindowState);
    }

    /// <summary>Off macOS the title bar has both, so the in-window menu hides them and their separator. The native
    /// items are only attached on macOS, so they are checked there.</summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Minimize_and_zoom_are_shown_only_on_macos(bool isMacOS)
    {
        var rig = Show(MenuStyle.Both, isMacOS);

        Assert.Equal(isMacOS, rig.Window.FindControl<MenuItem>("MinimizeMenuItem")!.IsVisible);
        Assert.Equal(isMacOS, rig.Window.FindControl<MenuItem>("ZoomMenuItem")!.IsVisible);
        Assert.Equal(isMacOS, rig.Window.FindControl<Separator>("MinimizeSeparator")!.IsVisible);
        if (isMacOS)
        {
            Assert.True(Native(rig.Window, "_Minimize").IsVisible);
            Assert.True(Native(rig.Window, "_Zoom").IsVisible);
        }
    }

    /// <summary>Switch Session... carries the command chord in both menus (Find's arrangement); Minimize carries Cmd+M
    /// on the native item only, since nothing else would dispatch it (spec §6, §7.4).</summary>
    [AvaloniaFact]
    public void Switch_session_carries_the_command_chord_and_minimize_carries_cmd_m()
    {
        var rig = Show();
        var command = rig.Window.GetPlatformSettings()!.HotkeyConfiguration.CommandModifiers;

        Assert.Equal(new KeyGesture(Key.K, command), Native(rig.Window, "_Switch Session...").Gesture);
        Assert.Equal(new KeyGesture(Key.K, command), rig.Window.FindControl<MenuItem>("SwitchSessionMenuItem")!.InputGesture);
        Assert.Equal(new KeyGesture(Key.M, KeyModifiers.Meta), Native(rig.Window, "_Minimize").Gesture);
        Assert.Null(rig.Window.FindControl<MenuItem>("MinimizeMenuItem")!.InputGesture);
    }
}
