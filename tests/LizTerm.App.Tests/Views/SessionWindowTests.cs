using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    public void File_transfer_click_opens_the_dialog_over_the_session_window_only_while_connected()
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
        transfer.StartCommand.Execute(null);
        Assert.Equal("A.B", vm.LastTransferRequest?.HostFile);
        dialog.Close();
        Assert.Empty(window.OwnedWindows);
    }
}
