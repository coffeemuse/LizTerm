// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LizTerm.App.Sessions;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Controls;

/// <summary>The switcher overlay. Keypad's contract: it raises Chosen and Dismissed and brings nothing itself;
/// SessionWindow decides what closing and choosing mean. Keys are taken on the tunnel, ahead of the TextBox, and a
/// jumping digit is taken from TextInput — see Task 6's note on why never from KeyDown.</summary>
public partial class SessionSwitcher : UserControl
{
    private SessionSwitcherViewModel? _viewModel;

    public SessionSwitcher()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnTunnelTextInput, RoutingStrategies.Tunnel);
    }

    /// <summary>A session was picked: by a jumping digit, Enter, or a click on its row.</summary>
    public event EventHandler<SessionEntry>? Chosen;

    /// <summary>Escape, Cmd/Ctrl+K, or a click on the dimmed screen.</summary>
    public event EventHandler? Dismissed;

    /// <summary>The filter box, for SessionWindow's clipboard guards: it lives in this control's name scope, so
    /// the window cannot reach it by name.</summary>
    internal TextBox Box => SwitcherBox;

    /// <summary>Lays out first. The window declares this control hidden, so until it is first shown it has never
    /// been measured, and a UserControl's content joins the visual tree only when its template is applied during
    /// layout; FocusManager refuses a box that is not attached, and the keystrokes meant for the filter would go
    /// to the screen and the host instead. Synchronous, so no key can arrive between showing and focusing.</summary>
    public void FocusBox()
    {
        UpdateLayout();
        SwitcherBox.Focus();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = DataContext as SessionSwitcherViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelChanged;
    }

    /// <summary>Keeps the selected row in view when the arrow keys move past the scrolled part of a long list.</summary>
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionSwitcherViewModel.Selected) && _viewModel?.Selected is { } row)
            RowsHost.ContainerFromItem(row)?.BringIntoView();
    }

    private void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is not { IsOpen: true } vm) return;
        var command = this.GetPlatformSettings()?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        switch (e.Key)
        {
            case Key.Up:
                vm.MoveUp();
                break;
            case Key.Down:
                vm.MoveDown();
                break;
            case Key.Enter:
                if (vm.Choose() is { } chosen) Chosen?.Invoke(this, chosen);
                break;
            case Key.Escape:
                Dismissed?.Invoke(this, EventArgs.Empty);
                break;
            case Key.K when e.KeyModifiers == command:
                Dismissed?.Invoke(this, EventArgs.Empty);
                break;
            case Key.Tab:
                // Tab and Shift+Tab: the palette has one field, so there is nowhere to tab to. Left to keyboard
                // navigation, focus would leave the box for the window's menu bar, and the window would close the
                // switcher behind it.
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OnTunnelTextInput(object? sender, TextInputEventArgs e)
    {
        if (_viewModel is not { IsOpen: true, DigitsJump: true } vm) return;
        if (e.Text is not { Length: 1 } text || !char.IsAsciiDigit(text[0])) return;
        e.Handled = true;
        if (vm.TryDigit(text[0]) is { } chosen) Chosen?.Invoke(this, chosen);
    }

    private void OnRowReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left || sender is not Control { DataContext: SwitcherRow row }) return;
        e.Handled = true;
        Chosen?.Invoke(this, row.Entry);
    }

    private void OnDimPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        Dismissed?.Invoke(this, EventArgs.Empty);
    }
}
