// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Input;
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

    public ProfilePickerWindow(ProfileStore store, Action<SessionProfile> openSession, Action quit) : this()
    {
        DataContext = new ProfilePickerViewModel(
            store,
            openSession,
            existing => new ProfileEditorWindow(existing).ShowDialog<ProfileEdit?>(this),
            quit);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ProfilePickerViewModel vm && vm.ConnectCommand.CanExecute(null)) vm.ConnectCommand.Execute(null);
    }
}
