// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using LizTerm.App.ViewModels;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Views;

public partial class ProfilePickerWindow : Window
{
    public ProfilePickerWindow()
    {
        InitializeComponent();
        // A picker opened from File > New Session sits alongside running sessions, and nothing tells it when one
        // writes a pin into a profile. Reload() already preserves the selection by name; the cost is a directory
        // read on focus.
        Activated += (_, _) => (DataContext as ProfilePickerViewModel)?.Reload();
    }

    public ProfilePickerWindow(ProfileStore store, Action<SessionProfile, bool> openSession, Action quit, TagRegistryStore? tags = null) : this()
    {
        DataContext = new ProfilePickerViewModel(
            store,
            openSession,
            existing => new ProfileEditorWindow(existing).ShowDialog<ProfileEdit?>(this),
            quit,
            tags);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ProfilePickerViewModel vm && vm.ConnectCommand.CanExecute(null)) vm.ConnectCommand.Execute(null);
    }

    /// <summary>Control-click is how a one-button Mac right-clicks, and Avalonia's macOS backend delivers it as a
    /// plain left press carrying the Control modifier (AvnView.mm maps mouseDown: to LeftButtonDown outright), so
    /// nothing raises ContextRequested and the row menu never opens. Raised here on the row's container, the same
    /// event a right button release raises. macOS only: elsewhere Control is the selection-toggle modifier, and a
    /// right-click habit already has a right button. The row is selected by then — Avalonia selects on the press,
    /// and the macOS toggle modifier is Cmd, not Control.</summary>
    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!OperatingSystem.IsMacOS() || e.InitialPressMouseButton != MouseButton.Left
            || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { } item)
        {
            item.RaiseEvent(new ContextRequestedEventArgs(e));
        }
    }

    /// <summary>Enter connects what is in the box. Handled here so the window's default button — Connect, for
    /// the profile selected in the list — never sees it: a user who types a host and presses Enter must not be
    /// connected somewhere else. Same shape as SessionWindow.OnFindBoxKeyDown.</summary>
    private void OnQuickConnectKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        e.Handled = true;
        (DataContext as ProfilePickerViewModel)?.QuickConnectCommand.Execute(null);
    }
}
