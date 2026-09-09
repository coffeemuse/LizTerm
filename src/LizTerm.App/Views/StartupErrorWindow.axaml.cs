// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LizTerm.App.Views;

/// <summary>Spec 7: when the engine is missing or not executable the splash gives way to this, not the picker.</summary>
public partial class StartupErrorWindow : Window
{
    public StartupErrorWindow() : this("") { }

    public StartupErrorWindow(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
    }

    private void OnQuitClick(object? sender, RoutedEventArgs e) => Close();
}
