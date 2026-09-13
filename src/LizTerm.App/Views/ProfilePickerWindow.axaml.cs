// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
        // Tunnel, not the XAML KeyDown: the ComboBox handles Enter and Delete itself, and a bubbling handler would
        // never see them.
        QuickConnectBox.AddHandler(KeyDownEvent, OnQuickConnectKeyDown, RoutingStrategies.Tunnel);
        // A picker opened from File > New Session sits alongside running sessions, and nothing tells it when one
        // writes a pin into a profile. Reload() already preserves the selection by name; the cost is a directory
        // read on focus.
        Activated += (_, _) => (DataContext as ProfilePickerViewModel)?.Reload();
    }

    public ProfilePickerWindow(ProfileStore store, Action<SessionProfile, bool> openSession, Action quit, TagRegistryStore? tags = null,
        RecentHostsStore? recentHosts = null) : this()
    {
        var vm = new ProfilePickerViewModel(
            store,
            openSession,
            existing => new ProfileEditorWindow(existing).ShowDialog<ProfileEdit?>(this),
            quit,
            tags,
            // Modal over this picker, so it cannot be open at the same time as the picker's own editor (spec 2.1).
            tags is null
                ? null
                : () => new ManageTagsWindow(new ManageTagsViewModel(new TagMaintenance(store, tags))).ShowDialog(this),
            recentHosts);
        DataContext = vm;
        // An open drop-down with nothing left in it is an empty box hanging under the field.
        vm.RecentEntries.CollectionChanged += (_, _) =>
        {
            if (vm.RecentEntries.Count == 0) QuickConnectBox.IsDropDownOpen = false;
        };
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
    /// connected somewhere else. Same shape as SessionWindow.OnFindBoxKeyDown. With the drop-down open too:
    /// highlighting an entry is what put it in the box, so one Enter recalls and connects, and the editable
    /// ComboBox never closes its own list on Enter (Avalonia 12.1.2), so this does. Delete forgets the highlighted
    /// entry only while the list is open; with it closed, Delete is ordinary text editing.</summary>
    private void OnQuickConnectKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ProfilePickerViewModel vm) return;
        if (e.Key is Key.Enter or Key.Return)
        {
            e.Handled = true;
            QuickConnectBox.IsDropDownOpen = false;
            vm.QuickConnectCommand.Execute(null);
        }
        else if (e.Key == Key.Delete && QuickConnectBox.IsDropDownOpen && Highlighted(e) is { } entry)
        {
            e.Handled = true;
            vm.RemoveRecentHostCommand.Execute(entry);
        }
    }

    /// <summary>The entry the keyboard is on: the item holding focus when the drop-down has taken it, else the
    /// ComboBox's own selection, which is what its arrow keys move.</summary>
    private string? Highlighted(KeyEventArgs e) =>
        (e.Source as Visual)?.FindAncestorOfType<ComboBoxItem>(includeSelf: true)?.DataContext as string
        ?? QuickConnectBox.SelectedItem as string;
}
