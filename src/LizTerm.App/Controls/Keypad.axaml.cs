// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using LizTerm.App.Keyboard;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Controls;

/// <summary>The on-screen keypad (keypad spec §4): 36 buttons built from KeypadLayout, raising KeyRequested the way
/// TerminalScreen does and knowing nothing about view models or sessions. The window routes the key (spec §6.2)
/// and binds IsVisible, IsEnabled and the two dock bindings (spec §6.1).</summary>
public partial class Keypad : UserControl
{
    /// <summary>Which way the grid is laid out: each bank a row (Bottom) or a column (Right).</summary>
    public static readonly StyledProperty<KeypadDock> DockProperty =
        AvaloniaProperty.Register<Keypad, KeypadDock>(nameof(Dock), KeypadDock.Bottom);

    /// <summary>The table the tooltips describe. Defaults to the built-in keymap; nothing binds it yet, since no
    /// keypad key depends on the one thing that varies the default (the backspace choice). The hook for #18.</summary>
    public static readonly StyledProperty<Keymap> KeymapProperty =
        AvaloniaProperty.Register<Keypad, Keymap>(nameof(Keymap), DefaultKeymap.Create(destructiveBackspace: true));

    /// <summary>For the window's DockPanel.Dock binding: where the panel sits is the window's decision, so the
    /// control converts rather than docking itself.</summary>
    public static readonly IValueConverter DockPanelDock =
        new FuncValueConverter<KeypadDock, Avalonia.Controls.Dock>(dock =>
            dock == KeypadDock.Right ? Avalonia.Controls.Dock.Right : Avalonia.Controls.Dock.Bottom);

    /// <summary>In bank order, whatever the dock; Arrange reorders the grid's children, never this list.</summary>
    private readonly List<Button> _buttons = [];

    public Keypad()
    {
        InitializeComponent();
        foreach (var bank in KeypadLayout.Banks)
        {
            foreach (var entry in bank)
            {
                // Focusable = false is the rule that keeps the keyboard on the screen through a click (spec §4.1);
                // the window's refocus after each key is the guarantee behind it. Tag carries the key so one
                // handler serves every button.
                var button = new Button { Content = entry.Label, Tag = entry.Key, Focusable = false };
                button.Classes.Add("keypad");
                button.Click += OnButtonClick;
                _buttons.Add(button);
            }
        }
        Arrange(Dock);
        Describe(Keymap);
    }

    public KeypadDock Dock
    {
        get => GetValue(DockProperty);
        set => SetValue(DockProperty, value);
    }

    public Keymap Keymap
    {
        get => GetValue(KeymapProperty);
        set => SetValue(KeymapProperty, value);
    }

    /// <summary>A button was clicked. The window sends it through SendKeyAsync, the method, never the command.</summary>
    public event EventHandler<TerminalKey>? KeyRequested;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DockProperty) Arrange(change.GetNewValue<KeypadDock>());
        else if (change.Property == KeymapProperty) Describe(change.GetNewValue<Keymap>());
    }

    /// <summary>Bottom: BankSize columns, bank-major, stretched to the window's width. Right: one column per bank,
    /// index-major so PF1 to PF12 read down the first, top-aligned so the twelve rows keep their natural height
    /// beside the screen instead of stretching to fill it (spec §4.2, §4.3).</summary>
    private void Arrange(KeypadDock dock)
    {
        var banks = KeypadLayout.Banks;
        ButtonGrid.Children.Clear();
        if (dock == KeypadDock.Right)
        {
            ButtonGrid.Columns = banks.Count;
            for (var i = 0; i < KeypadLayout.BankSize; i++)
            {
                for (var b = 0; b < banks.Count; b++) ButtonGrid.Children.Add(_buttons[b * KeypadLayout.BankSize + i]);
            }
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        }
        else
        {
            ButtonGrid.Columns = KeypadLayout.BankSize;
            foreach (var button in _buttons) ButtonGrid.Children.Add(button);
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        }
    }

    /// <summary>A null format is the platform's registration: glyphs on macOS, words elsewhere (spec §5).</summary>
    private void Describe(Keymap keymap)
    {
        foreach (var button in _buttons) ToolTip.SetTip(button, KeymapHints.Describe(keymap, (TerminalKey)button.Tag!));
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e) =>
        KeyRequested?.Invoke(this, (TerminalKey)((Button)sender!).Tag!);
}
