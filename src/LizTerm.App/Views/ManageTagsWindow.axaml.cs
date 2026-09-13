// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Views;

public partial class ManageTagsWindow : Window
{
    /// <summary>For the XAML loader; the app always passes a view model.</summary>
    public ManageTagsWindow() => InitializeComponent();

    public ManageTagsWindow(ManageTagsViewModel viewModel) : this() => DataContext = viewModel;

    /// <summary>Enter renames, and is handled here so nothing else in the window acts on it — the same shape as the
    /// picker's Quick Connect box.</summary>
    private void OnNameBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        e.Handled = true;
        if (DataContext is ManageTagsViewModel vm && vm.RenameCommand.CanExecute(null)) vm.RenameCommand.Execute(null);
    }

    /// <summary>The swatch's data context is its SwatchOption, so the colour is read straight off it.</summary>
    private void OnSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ManageTagsViewModel vm && (sender as Button)?.DataContext is SwatchOption swatch
            && vm.RecolourCommand.CanExecute(swatch.Color))
        {
            vm.RecolourCommand.Execute(swatch.Color);
        }
    }

    private void OnDoneClick(object? sender, RoutedEventArgs e) => Close();
}
