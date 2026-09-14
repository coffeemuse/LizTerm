// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LizTerm.App.Views;

/// <summary>The photo of Liz, the cat LizTerm is named for, shown modally over About from its dedication link.</summary>
public partial class LizWindow : Window
{
    public LizWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
