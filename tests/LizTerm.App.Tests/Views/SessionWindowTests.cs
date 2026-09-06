using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LizTerm.App.Controls;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

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

    /// <summary>Regression: correcting IsWireLogging from inside its own change notification is invisible to the
    /// two-way binding, which is mid-write, so the menu kept a check mark for a log that never started and the
    /// next click was swallowed as a no-op. Uses the real dispatcher, as the app does.</summary>
    [AvaloniaFact]
    public void A_wire_log_that_fails_to_start_leaves_the_menu_unchecked_and_retryable()
    {
        var session = new FakeEmulatorSession { WireLogException = new IOException("disk on fire") };
        var vm = new SessionViewModel(session, a => Dispatcher.UIThread.Post(a), new FakeTextClipboard());
        var directory = Path.Combine(Path.GetTempPath(), "lizterm-menu-" + Guid.NewGuid().ToString("N"));
        vm.WireLogDirectory = directory;
        var window = new SessionWindow { DataContext = vm };
        window.Show();
        var item = window.FindControl<MenuItem>("WireLogMenuItem")!;
        try
        {
            // The user ticks the item: the binding writes true into the view model, and the start fails.
            item.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.IsWireLogging);
            Assert.False(item.IsChecked);
            Assert.Equal("Could not open the wire log: disk on fire", vm.ErrorMessage);
            Assert.Equal("", vm.WireLogText);

            // The next tick must try again rather than being swallowed as a no-op.
            session.WireLogException = null;
            item.IsChecked = true;
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
        Assert.StartsWith("Could not open the File Transfer dialog:", vm.ErrorMessage);
    }
}
