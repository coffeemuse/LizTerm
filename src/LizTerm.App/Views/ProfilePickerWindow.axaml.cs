// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    private void OnRowConnectClick(object? sender, RoutedEventArgs e)
    {
        if (SelectMenuRow(sender) is { } vm && vm.ConnectCommand.CanExecute(null)) vm.ConnectCommand.Execute(null);
    }

    private void OnRowEditClick(object? sender, RoutedEventArgs e)
    {
        if (SelectMenuRow(sender) is { } vm && vm.EditCommand.CanExecute(null)) vm.EditCommand.Execute(null);
    }

    private void OnRowFavoriteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProfilePickerViewModel vm && (sender as MenuItem)?.DataContext is ProfileRow row
            && vm.ToggleFavoriteCommand.CanExecute(row))
        {
            vm.ToggleFavoriteCommand.Execute(row);
        }
    }

    /// <summary>Selects the row a menu entry belongs to, because Connect and Edit act on the selection. A right
    /// click already selects its row, but a menu opened from the keyboard acts on the focused one. By name
    /// rather than by instance: a reload rebuilds every row, and a row the list no longer holds cannot be
    /// selected — it would come back as null through the two-way binding.</summary>
    private ProfilePickerViewModel? SelectMenuRow(object? sender)
    {
        if (DataContext is not ProfilePickerViewModel vm || (sender as MenuItem)?.DataContext is not ProfileRow row) return null;
        vm.SelectedRow = vm.VisibleRows.FirstOrDefault(r => r.Name == row.Name);
        return vm;
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
