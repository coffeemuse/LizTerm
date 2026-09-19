// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.Dialogs;

namespace LizTerm.App.Views;

/// <summary>Asked before a session window closes, or the app quits, while a session is connected (#151). Keep
/// Connected is the default and Escape maps to it; closing with the title bar keeps the session too, so the only
/// way to a disconnect is the button that says so. The words come from the request.</summary>
public partial class ConfirmCloseWindow : Window
{
    /// <summary>Design-time only.</summary>
    public ConfirmCloseWindow() : this(ClosePromptRequest.ForWindow("MVS/CE")) { }

    public ConfirmCloseWindow(ClosePromptRequest request)
    {
        InitializeComponent();
        Title = request.Title;
        MessageText.Text = request.Message;
        DisconnectButton.Content = request.DisconnectLabel;
        KeepButton.Content = ClosePromptRequest.KeepLabel;
    }

    private void OnDisconnectClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnKeepClick(object? sender, RoutedEventArgs e) => Close(false);
}
