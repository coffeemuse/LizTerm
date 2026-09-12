// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using LizTerm.App.Controls;
using LizTerm.App.Keyboard;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Controls;

/// <summary>The control on its own (keypad spec §4): buttons from the layout table, one event, and nothing about
/// sessions. The window's half is in SessionWindowTests.</summary>
public class KeypadTests
{
    private static (Keypad Keypad, Window Window) Show(KeypadDock dock = KeypadDock.Bottom)
    {
        var keypad = new Keypad { Dock = dock };
        var window = new Window { Width = 960, Height = 680, Content = keypad };
        window.Show();
        return (keypad, window);
    }

    private static UniformGrid Grid(Keypad keypad) => keypad.FindControl<UniformGrid>("ButtonGrid")!;

    /// <summary>The border carries the dock's vertical alignment, not the control: how a host aligns the keypad is
    /// the host's business (keypad spec §4.2).</summary>
    private static Border BorderOf(Keypad keypad) => keypad.FindControl<Border>("KeypadBorder")!;

    private static TerminalKey KeyOf(Control child) => (TerminalKey)((Button)child).Tag!;

    [AvaloniaFact]
    public void One_button_per_layout_entry_in_bank_order_at_the_bottom()
    {
        var (keypad, _) = Show();
        var grid = Grid(keypad);

        Assert.Equal(36, grid.Children.Count);
        Assert.Equal(KeypadLayout.BankSize, grid.Columns);
        Assert.Equal(KeypadLayout.Banks.SelectMany(b => b).Select(k => k.Key), grid.Children.Select(KeyOf));
        Assert.Equal("PF2", ((Button)grid.Children[1]).Content);
    }

    /// <summary>Index-major: the same 36 buttons, each bank a column, so PF1 to PF12 read down the first.</summary>
    [AvaloniaFact]
    public void On_the_right_each_bank_is_a_column()
    {
        var (keypad, _) = Show(KeypadDock.Right);
        var grid = Grid(keypad);

        Assert.Equal(3, grid.Columns);
        Assert.Equal(TerminalKey.PF1, KeyOf(grid.Children[0]));
        Assert.Equal(TerminalKey.PF13, KeyOf(grid.Children[1]));
        Assert.Equal(TerminalKey.PA1, KeyOf(grid.Children[2]));
        Assert.Equal(TerminalKey.PF2, KeyOf(grid.Children[3]));
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Top, BorderOf(keypad).VerticalAlignment);
    }

    [AvaloniaFact]
    public void Changing_the_dock_relays_the_same_buttons()
    {
        var (keypad, _) = Show();
        var grid = Grid(keypad);
        var before = grid.Children.ToArray();

        keypad.Dock = KeypadDock.Right;
        Assert.Equal(3, grid.Columns);
        Assert.Equal(before.ToHashSet(), grid.Children.ToHashSet());

        keypad.Dock = KeypadDock.Bottom;
        Assert.Equal(KeypadLayout.BankSize, grid.Columns);
        Assert.Equal(before, grid.Children.ToArray());
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Stretch, BorderOf(keypad).VerticalAlignment);
    }

    /// <summary>Two clicks, two keys: no command disables in between (spec §4.1). And no button can take the
    /// keyboard: Focusable is false on every one.</summary>
    [AvaloniaFact]
    public void A_click_raises_the_button_key_and_no_button_is_focusable()
    {
        var (keypad, _) = Show();
        var raised = new List<TerminalKey>();
        keypad.KeyRequested += (_, key) => raised.Add(key);
        var pf13 = Grid(keypad).Children.Cast<Button>().Single(b => KeyOf(b) == TerminalKey.PF13);

        pf13.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        pf13.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal([TerminalKey.PF13, TerminalKey.PF13], raised);
        Assert.All(Grid(keypad).Children, child => Assert.False(((Button)child).Focusable));
    }

    /// <summary>The #18 hook: the tooltips are rebuilt from whatever Keymap the property holds.</summary>
    [AvaloniaFact]
    public void Tooltips_follow_the_keymap_property()
    {
        var (keypad, _) = Show();
        var buttons = Grid(keypad).Children.Cast<Button>().ToArray();
        var pa1 = buttons.Single(b => KeyOf(b) == TerminalKey.PA1);
        var dup = buttons.Single(b => KeyOf(b) == TerminalKey.Dup);
        Assert.NotNull(ToolTip.GetTip(pa1));
        Assert.Null(ToolTip.GetTip(dup));

        keypad.Keymap = DefaultKeymap.Create(destructiveBackspace: true)
            .With([KeyValuePair.Create(new KeyChord(Key.F9), TerminalKey.Dup)], []);

        Assert.NotNull(ToolTip.GetTip(dup));
    }

    /// <summary>The #18 hook again: that binding can hand the property a null before a live keymap exists, and a
    /// control that threw from its own property-changed callback would take the window down with it.</summary>
    [AvaloniaFact]
    public void A_null_keymap_keeps_the_tooltips_the_control_has()
    {
        var (keypad, _) = Show();
        var pa1 = Grid(keypad).Children.Cast<Button>().Single(b => KeyOf(b) == TerminalKey.PA1);
        var before = ToolTip.GetTip(pa1);

        keypad.Keymap = null!;

        Assert.Equal(before, ToolTip.GetTip(pa1));
    }

    /// <summary>Nothing is built for a keypad that is never shown: it is off by default, so most windows pay for no
    /// buttons and no tooltips at all.</summary>
    [AvaloniaFact]
    public void A_keypad_that_is_never_shown_builds_no_buttons()
    {
        var keypad = new Keypad { IsVisible = false };
        var window = new Window { Width = 960, Height = 680, Content = keypad };
        window.Show();
        Assert.Empty(Grid(keypad).Children);

        keypad.IsVisible = true;

        Assert.Equal(36, Grid(keypad).Children.Count);
    }
}
