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
}
