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
/// and binds IsVisible, IsEnabled, ShowPfKeys and the two dock bindings (spec §6.1).</summary>
public partial class Keypad : UserControl
{
    /// <summary>Which way the grid is laid out: each bank a row (Bottom) or a column (Right).</summary>
    public static readonly StyledProperty<KeypadDock> DockProperty =
        AvaloniaProperty.Register<Keypad, KeypadDock>(nameof(Dock), KeypadDock.Bottom);

    /// <summary>Whether the two banks of PF keys are in the grid (#105). Off leaves the third bank, the keys a
    /// keyboard with F-keys still has no key for.</summary>
    public static readonly StyledProperty<bool> ShowPfKeysProperty =
        AvaloniaProperty.Register<Keypad, bool>(nameof(ShowPfKeys), true);

    /// <summary>The table the tooltips describe. Defaults to the built-in keymap; SessionWindow sets it to the
    /// window's composed map (the profile's Backspace choice under the user's keymap.json, #18) on open and on
    /// every change.</summary>
    public static readonly StyledProperty<Keymap> KeymapProperty =
        AvaloniaProperty.Register<Keypad, Keymap>(nameof(Keymap), DefaultKeymap.Create(destructiveBackspace: true));

    /// <summary>For the window's DockPanel.Dock binding: where the panel sits is the window's decision, so the
    /// control converts rather than docking itself.</summary>
    public static readonly IValueConverter DockPanelDock =
        new FuncValueConverter<KeypadDock, Avalonia.Controls.Dock>(dock =>
            dock == KeypadDock.Right ? Avalonia.Controls.Dock.Right : Avalonia.Controls.Dock.Bottom);

    /// <summary>The buttons, one list per bank and in bank order, whatever the dock; Relayout reorders the grid's
    /// children, never these lists. Per bank rather than one flat list because that is the shape the layout reads:
    /// nothing then does arithmetic on BankSize, which only KeypadLayout's table promises. Empty until Build runs.</summary>
    private readonly List<List<Button>> _banks = [];

    public Keypad() => InitializeComponent();

    public KeypadDock Dock
    {
        get => GetValue(DockProperty);
        set => SetValue(DockProperty, value);
    }

    public bool ShowPfKeys
    {
        get => GetValue(ShowPfKeysProperty);
        set => SetValue(ShowPfKeysProperty, value);
    }

    public Keymap Keymap
    {
        get => GetValue(KeymapProperty);
        set => SetValue(KeymapProperty, value);
    }

    /// <summary>A button was clicked. The window sends it through SendKeyAsync, the method, never the command.</summary>
    public event EventHandler<TerminalKey>? KeyRequested;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (IsVisible) Build();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && change.GetNewValue<bool>()) Build();
        // Nothing built yet: Build reads every property itself, so there is nothing to bring up to date.
        else if (_banks.Count == 0) return;
        else if (change.Property == DockProperty || change.Property == ShowPfKeysProperty) Relayout();
        // A binding can hand a reference-typed styled property a null whatever its declared type says; the #18 hook
        // is exactly such a binding, so a keymap that is not there yet keeps the tooltips the control has.
        else if (change.Property == KeymapProperty && change.GetNewValue<Keymap>() is { } keymap) Describe(keymap);
    }

    /// <summary>The buttons, built the first time the keypad is shown rather than in the constructor: it is off by
    /// default (spec §2.1), and a window that never shows it should build no buttons and format no tooltips.</summary>
    private void Build()
    {
        if (_banks.Count > 0) return;
        foreach (var bank in KeypadLayout.Banks)
        {
            var buttons = new List<Button>(bank.Count);
            foreach (var entry in bank)
            {
                // Focusable = false is the rule that keeps the keyboard on the screen through a click (spec §4.1);
                // the window's refocus after each key is the guarantee behind it. Tag carries the key so one
                // handler serves every button.
                var button = new Button { Content = entry.Label, Tag = entry.Key, Focusable = false };
                button.Classes.Add("keypad");
                button.Click += OnButtonClick;
                buttons.Add(button);
            }
            _banks.Add(buttons);
        }
        Relayout();
        Describe(Keymap);
    }

    /// <summary>Bottom: one column per button of the longest bank, bank-major, stretched to the window's width.
    /// Right: one column per bank, index-major so PF1 to PF12 read down the first, with the border top-aligned so
    /// the twelve rows keep their natural height beside the screen instead of stretching to fill it (spec §4.2,
    /// §4.3). The border's alignment rather than the control's: how a host aligns this control is the host's.
    /// Without the PF keys, a bank holding nothing else is left out of the grid but kept, so turning them back on
    /// shows the same buttons.</summary>
    private void Relayout()
    {
        var dock = Dock;
        List<List<Button>> banks = ShowPfKeys ? _banks : [.. _banks.Where(bank => !bank.All(IsPfKey))];
        var longest = banks.Max(bank => bank.Count);
        ButtonGrid.Children.Clear();
        if (dock == KeypadDock.Right)
        {
            ButtonGrid.Columns = banks.Count;
            for (var i = 0; i < longest; i++)
            {
                foreach (var bank in banks)
                {
                    if (i < bank.Count) ButtonGrid.Children.Add(bank[i]);
                }
            }
        }
        else
        {
            ButtonGrid.Columns = longest;
            foreach (var button in banks.SelectMany(bank => bank)) ButtonGrid.Children.Add(button);
        }
        KeypadBorder.VerticalAlignment = dock == KeypadDock.Right
            ? Avalonia.Layout.VerticalAlignment.Top
            : Avalonia.Layout.VerticalAlignment.Stretch;
    }

    private static bool IsPfKey(Button button) => (TerminalKey)button.Tag! is >= TerminalKey.PF1 and <= TerminalKey.PF24;

    /// <summary>A null format is the platform's registration: glyphs on macOS, words elsewhere (spec §5). The
    /// keymap is reversed once for all 36 buttons rather than once per button.</summary>
    private void Describe(Keymap keymap)
    {
        var chords = keymap.Keys.ToLookup(pair => pair.Value, pair => pair.Key);
        foreach (var button in _banks.SelectMany(bank => bank))
        {
            ToolTip.SetTip(button, KeymapHints.Describe(chords[(TerminalKey)button.Tag!]));
        }
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e) =>
        KeyRequested?.Invoke(this, (TerminalKey)((Button)sender!).Tag!);
}
