// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LizTerm.App.Controls;
using LizTerm.App.Rendering;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Views;

/// <summary>The seam between the control and the view model: the two-way Selection binding and the hotkey wiring,
/// exercised through the real SessionWindow with the fakes behind it.</summary>
public class SessionWindowTests
{
    private static (SessionWindow Window, TerminalScreen Screen, SessionViewModel Vm, FakeEmulatorSession Session, FakeTextClipboard Clipboard) Show()
    {
        var session = new FakeEmulatorSession();
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(2, 3, "hello", null, null, null);
        session.CurrentScreen = buffer.Snapshot();
        var clipboard = new FakeTextClipboard();
        var vm = new SessionViewModel(session, action => action(), clipboard);
        var window = new SessionWindow { DataContext = vm };
        window.Show();
        var screen = window.FindControl<TerminalScreen>("Screen")!;
        screen.Focus();
        return (window, screen, vm, session, clipboard);
    }

    // CellRect is in TerminalScreen's own coordinate space; the control sits below the menu inside
    // SessionWindow's DockPanel, so window.MouseDown (which expects window-relative points) needs the
    // point translated through the visual tree rather than used as-is (unlike the Controls tests, where
    // the control is the window's sole Content and the two spaces coincide).
    private static Point Center(SessionWindow window, TerminalScreen screen, int row, int column)
    {
        var rect = screen.LastGeometry.CellRect(row, column);
        var local = new Point(rect.Center.X, rect.Center.Y);
        return screen.TranslatePoint(local, window) ?? local;
    }

    /// <summary>The window's half of the bell: the view model's BellRang reaches Screen.Flash(). The sound never
    /// passes through the window (bell spec §4).</summary>
    [AvaloniaFact]
    public void A_bell_from_the_view_model_flashes_the_screen()
    {
        var (_, screen, _, session, _) = Show();
        Assert.False(screen.BellFlashing);

        session.RaiseBell();

        Assert.True(screen.BellFlashing);
    }

    /// <summary>A view model that outlives its window must not flash a control that is gone: swapping the data
    /// context unsubscribes from the old one.</summary>
    [AvaloniaFact]
    public void A_replaced_view_model_no_longer_reaches_the_screen()
    {
        var (window, screen, _, oldSession, _) = Show();
        var newVm = new SessionViewModel(new FakeEmulatorSession(), action => action(), new FakeTextClipboard());

        window.DataContext = newVm;
        oldSession.RaiseBell();

        Assert.False(screen.BellFlashing);
    }

    [AvaloniaFact]
    public void A_closed_window_no_longer_flashes_on_a_bell()
    {
        var (window, screen, _, session, _) = Show();

        window.Close();
        session.RaiseBell();

        Assert.False(screen.BellFlashing);
    }

    private static void Drag(SessionWindow window, TerminalScreen screen)
    {
        window.MouseDown(Center(window, screen, 2, 3), MouseButton.Left);
        window.MouseMove(Center(window, screen, 5, 10));
        window.MouseUp(Center(window, screen, 5, 10), MouseButton.Left);
    }

    [AvaloniaFact]
    public async Task Drag_reaches_the_view_model_and_host_input_clears_the_control()
    {
        var (window, screen, vm, session, _) = Show();

        Drag(window, screen);
        Assert.Equal(ScreenRegion.FromCorners(2, 3, 5, 10), vm.Selection);

        await vm.SendKeyCommand.ExecuteAsync(TerminalKey.Enter);
        Assert.Null(screen.Selection);
        Assert.Contains("key:Enter", session.Calls);
    }

    [AvaloniaFact]
    public void Copy_hotkey_reaches_the_clipboard_through_the_command()
    {
        var (window, screen, vm, _, clipboard) = Show();
        Drag(window, screen);

        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);

        Assert.Equal("hello\n\n\n", clipboard.Text);
        Assert.Equal(ScreenRegion.FromCorners(2, 3, 5, 10), vm.Selection);
    }

    [AvaloniaFact]
    public void Paste_hotkey_is_a_no_op_while_disconnected_and_pastes_when_connected()
    {
        var (window, _, _, session, clipboard) = Show();
        clipboard.Text = "claude";

        window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);
        Assert.Empty(session.Calls);

        session.RaiseConnection(ConnectionState.Connected3270);
        window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);
        Assert.Equal(["paste:claude"], session.Calls);
    }

    [AvaloniaFact]
    public void Select_all_hotkey_sets_the_full_region_on_both_sides()
    {
        var (window, screen, vm, _, _) = Show();

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

        Assert.Equal(ScreenRegion.Full(24, 80), vm.Selection);
        Assert.Equal(ScreenRegion.Full(24, 80), screen.Selection);
    }

    /// <summary>Enter reaches NextAsync and Shift+Enter reaches PreviousAsync from the box itself, not just from
    /// FindViewModelTests' direct calls -- OnFindBoxKeyDown is what actually routes the two keys, and nothing
    /// end to end asserted that before. FakeEmulatorSession's move: records let this be told apart from a no-op:
    /// Next visits the first match without moving past it, so Previous from there wraps back to the second.</summary>
    [AvaloniaFact]
    public void Enter_walks_forward_and_shift_enter_walks_back_in_the_find_box()
    {
        var (window, _, vm, session, _) = Show();
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(2, 3, "ab", null, null, null);
        buffer.SetText(5, 3, "ab", null, null, null);
        session.RaiseScreen(buffer.Snapshot());
        vm.Find.Open();
        vm.Find.Term = "ab";
        window.FindControl<TextBox>("FindBox")!.Focus();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Contains("move:2,3", session.Calls);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);
        Assert.Contains("move:5,3", session.Calls);
    }

    /// <summary>The highest-consequence invariant of the whole find feature: nothing typed into the box ever
    /// reaches the host. Only the macOS native-menu paste guard (NativeMenuTests) currently covers a case where
    /// typing-shaped input stays out of the session while the box has focus; this covers plain keystrokes through
    /// the control that opens the bar for every platform, native menu or classic.</summary>
    [AvaloniaFact]
    public void Typing_in_the_find_box_sends_nothing_to_the_host()
    {
        var (window, _, vm, session, _) = Show();
        session.RaiseConnection(ConnectionState.Connected3270);
        vm.Find.Open();
        var box = window.FindControl<TextBox>("FindBox")!;
        box.Focus();

        window.KeyTextInput("hello");

        Assert.Equal("hello", box.Text);
        Assert.Equal("hello", vm.Find.Term);
        Assert.Empty(session.Calls);
    }

    [AvaloniaFact]
    public void File_transfer_menu_item_follows_the_connection_state()
    {
        var (window, _, _, session, _) = Show();
        var item = window.FindControl<MenuItem>("FileTransferMenuItem")!;
        Assert.False(item.IsEnabled);
        session.RaiseConnection(ConnectionState.Connected3270);
        Assert.True(item.IsEnabled);
        session.RaiseConnection(ConnectionState.Disconnected);
        Assert.False(item.IsEnabled);
    }

    [AvaloniaFact]
    public async Task File_transfer_click_opens_the_dialog_over_the_session_window_only_while_connected()
    {
        var (window, _, vm, session, _) = Show();
        var item = window.FindControl<MenuItem>("FileTransferMenuItem")!;

        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Empty(window.OwnedWindows);

        session.RaiseConnection(ConnectionState.Connected3270);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var dialog = Assert.Single(window.OwnedWindows);
        var transfer = Assert.IsType<FileTransferViewModel>(dialog.DataContext);
        Assert.True(transfer.IsForm);

        transfer.LocalPath = "/nonexistent/a.txt";
        transfer.HostFile = "A.B";
        await transfer.StartCommand.ExecuteAsync(null);
        Assert.Equal("A.B", vm.LastTransferRequest?.HostFile);
        Assert.True(transfer.IsDone);
        dialog.Close();
        Assert.Empty(window.OwnedWindows);
    }

    /// <summary>Regression: after Dismiss the button kept focus, so the next keystrokes never reached the host.</summary>
    [AvaloniaFact]
    public void Dismiss_returns_focus_to_the_screen()
    {
        var (window, screen, vm, _, _) = Show();
        vm.ErrorMessage = "boom";
        var dismiss = window.FindControl<Button>("DismissButton")!;
        dismiss.Focus();
        Assert.False(screen.IsFocused);

        dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(screen.IsFocused);
    }

    private static Keypad KeypadOf(SessionWindow window) => window.FindControl<Keypad>("KeypadPanel")!;

    private static Button KeypadButton(SessionWindow window, TerminalKey key) =>
        KeypadOf(window).FindControl<UniformGrid>("ButtonGrid")!.Children.Cast<Button>().Single(b => (TerminalKey)b.Tag! == key);

    private static Point CentreOf(SessionWindow window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    /// <summary>The whole route, by a real pointer press: the button's key reaches the host through SendKeyAsync,
    /// and the screen still has the keyboard afterwards — the buttons take no focus and the handler refocuses
    /// regardless (keypad spec §4.1, §6.2).</summary>
    [AvaloniaFact]
    public void A_keypad_click_reaches_the_host_and_leaves_the_screen_focused()
    {
        var (window, screen, vm, session, _) = Show();
        session.RaiseConnection(ConnectionState.Connected3270);
        vm.Settings.Keypad = true;
        window.UpdateLayout();
        var centre = CentreOf(window, KeypadButton(window, TerminalKey.PF3));

        window.MouseDown(centre, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(centre, MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(["key:PF3"], session.Calls);
        Assert.True(screen.IsFocused);
    }

    /// <summary>The handler's third step is the guarantee for when something else had the keyboard, the find box
    /// here; the buttons take no focus themselves, so this is the one path that exercises Screen.Focus(). Modelled
    /// on Dismiss_returns_focus_to_the_screen.</summary>
    [AvaloniaFact]
    public void A_keypad_click_returns_focus_from_the_find_box_to_the_screen()
    {
        var (window, screen, vm, session, _) = Show();
        session.RaiseConnection(ConnectionState.Connected3270);
        vm.Settings.Keypad = true;
        vm.Find.Open();
        var findBox = window.FindControl<TextBox>("FindBox")!;
        findBox.Focus();
        Assert.False(screen.IsFocused);

        KeypadButton(window, TerminalKey.PF3).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(screen.IsFocused);
        Assert.Equal(["key:PF3"], session.Calls);
    }

    /// <summary>Right Ctrl held across a keypad click: the key goes, and the release is not a tap. Without
    /// CancelTap the detector would see Ctrl down, nothing, Ctrl up, and send Enter (keypad spec §6.2). Driven by a
    /// real pointer press rather than a raised ClickEvent: the rule is about presses, and a synthetic click always
    /// reaches the handler, so it could never see a press that does not become one — the case below.</summary>
    [AvaloniaFact]
    public void Right_ctrl_held_across_a_keypad_click_sends_the_key_and_no_enter()
    {
        var (window, _, vm, session, _) = Show();
        session.RaiseConnection(ConnectionState.Connected3270);
        vm.Settings.Keypad = true;
        window.UpdateLayout();
        var centre = CentreOf(window, KeypadButton(window, TerminalKey.PF3));

        window.KeyPressQwerty(PhysicalKey.ControlRight, RawInputModifiers.Control);
        window.MouseDown(centre, MouseButton.Left, RawInputModifiers.Control);
        window.MouseUp(centre, MouseButton.Left, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.ControlRight, RawInputModifiers.None);

        Assert.Equal(["key:PF3"], session.Calls);
    }

    /// <summary>The same rule for the presses on the keypad that never become clicks, which is why the window ends
    /// the tap on any pointer press rather than on the keypad's Click: the border's padding and the margins between
    /// the buttons take no focus and raise no Click, and neither does a button the pointer leaves before releasing.
    /// Each half holds its own Ctrl, so neither is carried by the other's reset.</summary>
    [AvaloniaFact]
    public void Right_ctrl_held_across_a_keypad_press_that_is_not_a_click_sends_nothing()
    {
        var (window, _, vm, session, _) = Show();
        session.RaiseConnection(ConnectionState.Connected3270);
        vm.Settings.Keypad = true;
        window.UpdateLayout();
        var chrome = KeypadOf(window).TranslatePoint(new Point(2, 1), window)!.Value;
        var button = CentreOf(window, KeypadButton(window, TerminalKey.PF3));

        window.KeyPressQwerty(PhysicalKey.ControlRight, RawInputModifiers.Control);
        window.MouseDown(chrome, MouseButton.Left, RawInputModifiers.Control);
        window.MouseUp(chrome, MouseButton.Left, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.ControlRight, RawInputModifiers.None);
        Assert.Empty(session.Calls);

        window.KeyPressQwerty(PhysicalKey.ControlRight, RawInputModifiers.Control);
        window.MouseDown(button, MouseButton.Left, RawInputModifiers.Control);
        window.MouseMove(chrome, RawInputModifiers.Control);
        window.MouseUp(chrome, MouseButton.Left, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.ControlRight, RawInputModifiers.None);
        Assert.Empty(session.Calls);
    }

    [AvaloniaFact]
    public void The_keypad_is_hidden_by_default_and_follows_the_setting()
    {
        var (window, _, vm, _, _) = Show();
        var keypad = KeypadOf(window);
        Assert.False(keypad.IsVisible);

        vm.Settings.Keypad = true;
        Assert.True(keypad.IsVisible);

        vm.Settings.Keypad = false;
        Assert.False(keypad.IsVisible);
    }

    /// <summary>Two bindings to one setting: the panel's edge of the window and the control's own grid shape.</summary>
    [AvaloniaFact]
    public void The_keypad_docks_where_the_setting_says()
    {
        var (window, _, vm, _, _) = Show();
        var keypad = KeypadOf(window);
        Assert.Equal(Dock.Bottom, DockPanel.GetDock(keypad));
        Assert.Equal(KeypadDock.Bottom, keypad.Dock);

        vm.Settings.KeypadDock = KeypadDock.Right;

        Assert.Equal(Dock.Right, DockPanel.GetDock(keypad));
        Assert.Equal(KeypadDock.Right, keypad.Dock);
    }

    [AvaloniaFact]
    public void The_keypad_shows_the_pf_keys_while_the_setting_says()
    {
        var (window, _, vm, _, _) = Show();
        var keypad = KeypadOf(window);
        Assert.True(keypad.ShowPfKeys);

        vm.Settings.KeypadPfKeys = false;
        Assert.False(keypad.ShowPfKeys);

        vm.Settings.KeypadPfKeys = true;
        Assert.True(keypad.ShowPfKeys);
    }

    /// <summary>Greyed rather than silently inert: the keyboard and the Keys menu send nothing visible while
    /// disconnected (the engine's action error is swallowed), and the keypad invites more clicking than either.</summary>
    [AvaloniaFact]
    public void The_keypad_is_enabled_only_while_connected()
    {
        var (window, _, _, session, _) = Show();
        var keypad = KeypadOf(window);
        Assert.False(keypad.IsEnabled);

        session.RaiseConnection(ConnectionState.Connected3270);
        Assert.True(keypad.IsEnabled);

        session.RaiseConnection(ConnectionState.Disconnected);
        Assert.False(keypad.IsEnabled);
    }

    /// <summary>The keypad form of A_key_pressed_while_the_previous_one_is_in_flight_still_reaches_the_host: the
    /// method, not the command, so a second click while the first round trip is open still reaches the host.</summary>
    [AvaloniaFact]
    public async Task A_keypad_click_while_the_previous_key_is_in_flight_still_reaches_the_host()
    {
        var (window, _, vm, session, _) = Show();
        session.RaiseConnection(ConnectionState.Connected3270);
        vm.Settings.Keypad = true;
        session.SendKeyCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        KeypadButton(window, TerminalKey.PF1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        KeypadButton(window, TerminalKey.PF2).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(["key:PF1", "key:PF2"], session.Calls);
        session.SendKeyCompletion.SetResult();
        await Task.Yield();
    }

    [AvaloniaFact]
    public void Help_menu_has_the_wire_log_toggle_bound_to_the_view_model()
    {
        var (window, _, vm, _, _) = Show();
        var item = window.FindControl<MenuItem>("WireLogMenuItem")!;
        Assert.Equal(MenuItemToggleType.CheckBox, item.ToggleType);
        Assert.False(item.IsChecked);
        var directory = Path.Combine(Path.GetTempPath(), "lizterm-win-" + Guid.NewGuid().ToString("N"));
        vm.WireLogDirectory = directory;
        try
        {
            vm.IsWireLogging = true;
            Assert.True(item.IsChecked);
            Assert.Equal("● wire log", window.FindControl<TextBlock>("WireLogStatus")!.Text);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The menu item ticks its own check mark before the handler runs, and a declined warning changes
    /// nothing in the view model for a OneWay binding to follow — so without the re-notify in ToggleWireLogAsync
    /// the item would keep a check mark for a log the user just refused (#139).</summary>
    [AvaloniaFact]
    public void Declining_the_wire_log_warning_leaves_the_menu_unchecked()
    {
        var session = new FakeEmulatorSession();
        var vm = new SessionViewModel(session, a => Dispatcher.UIThread.Post(a), new FakeTextClipboard(),
            wireLogPrompt: new FakeWireLogPrompt { Confirm = false });
        var window = new SessionWindow { DataContext = vm };
        window.Show();
        var item = window.FindControl<MenuItem>("WireLogMenuItem")!;

        item.IsChecked = true;
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.IsWireLogging);
        Assert.False(item.IsChecked);
        Assert.Empty(session.Calls);
    }

    /// <summary>Regression: correcting IsWireLogging from inside its own change notification was invisible to the
    /// two-way binding this item used to carry, which is mid-write, so the menu kept a check mark for a log that
    /// never started and the next click was swallowed as a no-op. The item is Click-driven and OneWay now (#139),
    /// which removes that shape, but the behaviour it guards is the same and still worth pinning. Replays the real
    /// renderer's order — DefaultMenuInteractionHandler ticks IsChecked, then raises Click — and uses the real
    /// dispatcher, as the app does.</summary>
    [AvaloniaFact]
    public void A_wire_log_that_fails_to_start_leaves_the_menu_unchecked_and_retryable()
    {
        var session = new FakeEmulatorSession { WireLogException = new IOException("disk on fire") };
        var vm = new SessionViewModel(session, a => Dispatcher.UIThread.Post(a), new FakeTextClipboard(),
            wireLogPrompt: new FakeWireLogPrompt { Confirm = true });
        var directory = Path.Combine(Path.GetTempPath(), "lizterm-menu-" + Guid.NewGuid().ToString("N"));
        vm.WireLogDirectory = directory;
        var window = new SessionWindow { DataContext = vm };
        window.Show();
        var item = window.FindControl<MenuItem>("WireLogMenuItem")!;
        try
        {
            // The user ticks the item: the handler runs the command, the warning is accepted, the start fails.
            item.IsChecked = true;
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.IsWireLogging);
            Assert.False(item.IsChecked);
            Assert.Equal("Could not open the wire log: disk on fire", vm.ErrorMessage);
            Assert.Equal("", vm.WireLogText);

            // The next tick must try again rather than being swallowed as a no-op.
            session.WireLogException = null;
            item.IsChecked = true;
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IsWireLogging);
            Assert.True(item.IsChecked);
            Assert.Equal("● wire log", vm.WireLogText);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Regression: routing keys through CanExecute dropped any key that arrived while the previous key's
    /// round trip was still open, because the toolkit's async command reports CanExecute false while running.</summary>
    [AvaloniaFact]
    public async Task A_key_pressed_while_the_previous_one_is_in_flight_still_reaches_the_host()
    {
        var (window, _, _, session, _) = Show();
        session.SendKeyCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        window.KeyPressQwerty(PhysicalKey.F1, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);

        Assert.Equal(["key:PF1", "key:PF2"], session.Calls);
        session.SendKeyCompletion.SetResult();
        await Task.Yield();
    }

    /// <summary>OnFileTransferClick's catch: a dialog that cannot be shown is reported in the banner, not thrown
    /// from an async void handler. A window that was never shown is an owner ShowDialog refuses.</summary>
    [AvaloniaFact]
    public async Task A_file_transfer_dialog_that_cannot_open_is_reported_in_the_banner()
    {
        var session = new FakeEmulatorSession();
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard());
        var window = new SessionWindow { DataContext = vm };
        session.RaiseConnection(ConnectionState.Connected3270);

        window.FindControl<MenuItem>("FileTransferMenuItem")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        await Wait.UntilAsync(() => vm.ErrorMessage is not null, "the error banner");
        Assert.StartsWith("Could not open the IND$FILE Transfer dialog:", vm.ErrorMessage);
    }

    /// <summary>About from a session window describes that session's engine and is modal to it. One method
    /// serves this and the macOS application menu, so this is also the test that the shared spelling works.</summary>
    [AvaloniaFact]
    public void About_opens_over_the_session_window_that_asked_for_it()
    {
        var (window, _, _, _, _) = Show();
        var app = (LizTerm.App.App)Application.Current!;

        _ = app.ShowAboutAsync(window);

        var dialog = Assert.Single(window.OwnedWindows);
        var about = Assert.IsType<AboutWindow>(dialog);
        // The -DEV marker and the commit are useless if App passes something else here, and nothing else would
        // notice: AboutWindowTests builds its windows with hand-written strings (issue #141).
        Assert.Equal("Version " + AppVersion.Display, about.FindControl<TextBlock>("VersionText")!.Text);
        dialog.Close();
        Assert.Empty(window.OwnedWindows);
    }

    /// <summary>The field bug behind ModalDialogs: over a Keep on Top session, File Transfer opened beneath it on
    /// macOS, where only a Keep on Top dialog can draw above a Keep on Top window.</summary>
    [AvaloniaFact]
    public async Task File_transfer_over_a_keep_on_top_session_opens_keep_on_top()
    {
        var (window, _, _, session, _) = Show();
        session.RaiseConnection(ConnectionState.Connected3270);
        window.FindControl<MenuItem>("KeepOnTopMenuItem")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.True(window.Topmost);

        window.FindControl<MenuItem>("FileTransferMenuItem")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        await Wait.UntilAsync(() => window.OwnedWindows.Count == 1, "the File Transfer dialog");
        var dialog = Assert.IsType<FileTransferWindow>(Assert.Single(window.OwnedWindows));
        Assert.True(dialog.Topmost);
        dialog.Close();
    }

    /// <summary>A second About must not stack on the first. The session's own Help item cannot be reached while
    /// About is modal over that window, but the macOS menu bar stays live over a modal dialog, so the
    /// application menu could ask again — and the second dialog would be owned by the first and, since an
    /// AboutWindow is not a session, would report no engine.</summary>
    [AvaloniaFact]
    public void A_second_about_activates_the_one_already_open()
    {
        var (window, _, _, _, _) = Show();
        var app = (LizTerm.App.App)Application.Current!;

        _ = app.ShowAboutAsync(window);
        _ = app.ShowAboutAsync(window);

        var dialog = Assert.Single(window.OwnedWindows);
        dialog.Close();

        // And the guard lifts once it has closed, rather than locking About out for the session's life.
        _ = app.ShowAboutAsync(window);
        var reopened = Assert.Single(window.OwnedWindows);
        reopened.Close();
        Assert.Empty(window.OwnedWindows);
    }

    /// <summary>The engine About names. The owner is only the window in front, so resolving the engine from it
    /// would answer "no session" for the File Transfer dialog, the picker or the splash — and send About back to
    /// the located binary, reporting no version for an engine that has been running and has told us one.</summary>
    [Fact]
    public void About_falls_back_to_the_last_session_before_the_located_binary()
    {
        var running = new EngineInfo("b3270", "4.5.6", "/bundled/b3270", EngineSource.Bundled);
        var located = new EngineInfo("b3270", null, "", EngineSource.Unknown);
        var session = new SessionViewModel(new FakeEmulatorSession { Engine = running }, a => a(), new FakeTextClipboard());
        var notASession = new object();

        // The owner wins when it is a session: a window's own Help item describes that window.
        Assert.Equal(running, LizTerm.App.App.AboutEngine(session, null, () => located));
        // Otherwise the session last in front, whatever is on top now.
        Assert.Equal(running, LizTerm.App.App.AboutEngine(notASession, session, () => located));
        Assert.Equal(running, LizTerm.App.App.AboutEngine(null, session, () => located));
        // Only with no session anywhere does it fall back to the binary on disk.
        Assert.Equal(located, LizTerm.App.App.AboutEngine(notASession, notASession, () => located));
        Assert.Equal(located, LizTerm.App.App.AboutEngine(null, null, () => located));
    }

    // ----- The connect banner and the status bar's note icon and chips (#93) -----

    private static (SessionWindow Window, SessionViewModel Vm, FakeEmulatorSession Session) ShowProfile(SessionProfile profile, SettingsViewModel? settings = null)
    {
        var session = new FakeEmulatorSession { Profile = profile, CurrentScreen = new ScreenBuffer(24, 80).Snapshot() };
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard(), settings: settings);
        var window = new SessionWindow { DataContext = vm };
        window.Show();
        return (window, vm, session);
    }

    private static SessionProfile NotedAndTagged() => new()
    {
        Name = "MVS/CE", Host = "10.42.37.209", Port = 3270, Note = "no live data",
        Tags = TagSet.From(["prod", "mvs"]),
    };

    /// <summary>The icon is there for every session, even one with nothing but a name, so the banner is always
    /// one click away; its tooltip is the note, or nothing.</summary>
    [AvaloniaFact]
    public void The_note_icon_is_always_present_and_carries_the_note_as_its_tooltip()
    {
        var (noted, _, _) = ShowProfile(NotedAndTagged());
        var icon = noted.FindControl<Button>("NoteIcon")!;
        Assert.True(icon.IsVisible);
        Assert.Equal("no live data", ToolTip.GetTip(icon));

        var (bare, _, _) = ShowProfile(new SessionProfile { Name = "tk5", Host = "h" });
        var bareIcon = bare.FindControl<Button>("NoteIcon")!;
        Assert.True(bareIcon.IsVisible);
        Assert.Null(ToolTip.GetTip(bareIcon));
    }

    [AvaloniaFact]
    public void Clicking_the_note_icon_shows_the_banner()
    {
        var (window, vm, _) = ShowProfile(new SessionProfile { Name = "tk5", Host = "h" });
        var banner = window.FindControl<Border>("NoteBanner")!;
        Assert.False(banner.IsVisible);

        window.FindControl<Button>("NoteIcon")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(vm.IsBannerVisible);
        Assert.True(banner.IsVisible);
    }

    [AvaloniaFact]
    public void The_banner_shows_on_connect_with_the_chips_the_name_the_host_and_the_note()
    {
        var (window, _, session) = ShowProfile(NotedAndTagged());
        var banner = window.FindControl<Border>("NoteBanner")!;
        Assert.False(banner.IsVisible);

        session.RaiseConnection(ConnectionState.Connected3270);

        Assert.True(banner.IsVisible);
        Assert.Equal("MVS/CE", window.FindControl<TextBlock>("BannerName")!.Text);
        Assert.Equal("10.42.37.209:3270", window.FindControl<TextBlock>("BannerHostPort")!.Text);
        Assert.Equal("no live data", window.FindControl<TextBlock>("BannerNote")!.Text);
        Assert.Equal(["PROD", "MVS"], window.FindControl<ItemsControl>("BannerChips")!.ItemsSource!.Cast<TagChip>().Select(c => c.Text));
    }

    /// <summary>The chips in the bar itself are the one part behind a preference, and it applies live.</summary>
    [AvaloniaFact]
    public void The_status_bar_chips_follow_the_preference_and_default_off()
    {
        var settings = new SettingsViewModel();
        var (window, _, _) = ShowProfile(NotedAndTagged(), settings);
        var chips = window.FindControl<ItemsControl>("StatusChips")!;
        Assert.False(chips.IsVisible);
        Assert.Equal(["PROD", "MVS"], chips.ItemsSource!.Cast<TagChip>().Select(c => c.Text));

        settings.ShowTagsInStatusBar = true;
        Assert.True(chips.IsVisible);

        settings.ShowTagsInStatusBar = false;
        Assert.False(chips.IsVisible);
    }

    /// <summary>x3270 rules its OIA off from the screen with a line in the palette's blue. The bar does the same,
    /// so the only colours on it are the screen's own.</summary>
    [AvaloniaFact]
    public void The_status_bar_is_ruled_off_in_the_palettes_blue()
    {
        var (window, _, _, _, _) = Show();
        var bar = window.FindControl<Border>("StatusBar")!;
        Assert.Equal(new Thickness(0, 1, 0, 0), bar.BorderThickness);
        Assert.Same(Palette.OiaRule, bar.BorderBrush);
    }

    /// <summary>The bar's text, its operator errors and its padlock take the palette's colours, like the rule above
    /// them, so a palette change reaches the whole bar.</summary>
    [AvaloniaFact]
    public void The_status_bar_draws_in_the_palettes_colours()
    {
        var (window, _, _, session, _) = Show();
        session.RaiseConnection(ConnectionState.ConnectedTn3270E, new TlsInfo(true, true, null, null));
        session.RaiseStatus(new KeyboardStatus(KeyboardLock.ProtectedField, null, false, false, null));

        Assert.Same(Palette.OiaText, window.FindControl<TextBlock>("ModeField")!.Foreground);
        Assert.Same(Palette.OiaError, window.FindControl<TextBlock>("MessageArea")!.Foreground);
        Assert.Same(Palette.OiaError, window.FindControl<TextBlock>("WireLogStatus")!.Foreground);
        var tls = window.FindControl<StackPanel>("TlsField")!.Children.OfType<TextBlock>().ToList();
        Assert.Equal(2, tls.Count);
        Assert.All(tls, block => Assert.Same(Palette.TlsVerified, block.Foreground));

        session.RaiseConnection(ConnectionState.ConnectedTn3270E, new TlsInfo(true, false, null, null));
        Assert.All(tls, block => Assert.Same(Palette.TlsUnverified, block.Foreground));
    }
}
