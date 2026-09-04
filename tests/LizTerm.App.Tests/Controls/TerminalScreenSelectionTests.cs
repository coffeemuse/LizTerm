using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using LizTerm.App.Controls;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.Controls;

public class TerminalScreenSelectionTests
{
    private static (Window Window, TerminalScreen Screen) Show(ScreenSnapshot? snapshot = null)
    {
        var screen = new TerminalScreen { Snapshot = snapshot ?? ScreenSnapshot.Empty(24, 80) };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        screen.Focus();
        return (window, screen);
    }

    private static Avalonia.Point Center(TerminalScreen screen, int row, int column)
    {
        var rect = screen.LastGeometry.CellRect(row, column);
        return new Avalonia.Point(rect.Center.X, rect.Center.Y);
    }

    [AvaloniaFact]
    public void Drag_sets_a_normalized_selection_and_does_not_click()
    {
        var (window, screen) = Show();
        var clicked = false;
        screen.CellClicked += (_, _) => clicked = true;

        window.MouseDown(Center(screen, 5, 10), MouseButton.Left);
        window.MouseMove(Center(screen, 2, 3));
        window.MouseUp(Center(screen, 2, 3), MouseButton.Left);

        Assert.Equal(ScreenRegion.FromCorners(2, 3, 5, 10), screen.Selection);
        Assert.False(clicked);
    }

    [AvaloniaFact]
    public void Drag_past_the_edge_extends_the_selection_to_the_edge()
    {
        var (window, screen) = Show();
        window.MouseDown(Center(screen, 20, 70), MouseButton.Left);
        window.MouseMove(new Avalonia.Point(799, 599));
        window.MouseUp(new Avalonia.Point(799, 599), MouseButton.Left);
        Assert.Equal(ScreenRegion.FromCorners(20, 70, 23, 79), screen.Selection);
    }

    [AvaloniaFact]
    public void Click_moves_the_cursor_on_release_and_clears_the_selection()
    {
        var (window, screen) = Show();
        screen.Selection = ScreenRegion.FromCorners(0, 0, 1, 1);
        (int Row, int Column)? clicked = null;
        screen.CellClicked += (_, c) => clicked = c;

        window.MouseDown(Center(screen, 5, 12), MouseButton.Left);
        Assert.Null(clicked);
        window.MouseUp(Center(screen, 5, 12), MouseButton.Left);

        Assert.Equal((5, 12), clicked);
        Assert.Null(screen.Selection);
    }

    [AvaloniaFact]
    public void Double_click_selects_the_word_under_the_pointer()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(3, 10, "SYS1.PROCLIB", null, null, null);
        var (window, screen) = Show(buffer.Snapshot());
        var point = Center(screen, 3, 14);

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        Assert.Equal(ScreenRegion.FromCorners(3, 10, 3, 21), screen.Selection);
    }

    [AvaloniaFact]
    public void Snapshot_of_a_different_size_clears_the_selection_and_same_size_keeps_it()
    {
        var (_, screen) = Show();
        screen.Selection = ScreenRegion.FromCorners(1, 1, 2, 2);
        screen.Snapshot = ScreenSnapshot.Empty(24, 80);
        Assert.Equal(ScreenRegion.FromCorners(1, 1, 2, 2), screen.Selection);
        screen.Snapshot = ScreenSnapshot.Empty(43, 80);
        Assert.Null(screen.Selection);
    }

    [AvaloniaFact]
    public void Right_button_neither_selects_nor_clicks()
    {
        var (window, screen) = Show();
        var clicked = false;
        screen.CellClicked += (_, _) => clicked = true;
        window.MouseDown(Center(screen, 1, 1), MouseButton.Right);
        window.MouseMove(Center(screen, 4, 4));
        window.MouseUp(Center(screen, 4, 4), MouseButton.Right);
        Assert.Null(screen.Selection);
        Assert.False(clicked);
    }

    [AvaloniaFact]
    public void Clipboard_hotkeys_raise_requests_and_bypass_the_keymap()
    {
        var (window, screen) = Show();
        var events = new List<string>();
        screen.CopyRequested += (_, _) => events.Add("copy");
        screen.PasteRequested += (_, _) => events.Add("paste");
        screen.SelectAllRequested += (_, _) => events.Add("select-all");
        screen.KeyRequested += (_, k) => events.Add("key:" + k);
        screen.TextEntered += (_, t) => events.Add("text:" + t);

        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.None);

        Assert.Equal(["copy", "paste", "select-all", "key:PF3"], events);
    }
}
