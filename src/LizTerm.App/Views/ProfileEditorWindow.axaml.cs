// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.Dialogs;
using LizTerm.App.ViewModels;
using LizTerm.Core.Session;

namespace LizTerm.App.Views;

public partial class ProfileEditorWindow : Window
{
    public ProfileEditorWindow() : this(null) { }

    public ProfileEditorWindow(SessionProfile? existing)
    {
        InitializeComponent();
        DataContext = new ProfileEditorViewModel(existing, HostFileServiceFactory.CreateTester(new AvaloniaCredentialPrompt(this)));
        Title = existing is null ? "New Session Profile" : $"Edit {existing.Name}";
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProfileEditorViewModel vm && vm.TryBuild() is { } profile)
            Close(new ProfileEdit(profile, vm.PinCleared, vm.MvsmfPinCleared));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
