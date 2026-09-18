// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.Dialogs;

namespace LizTerm.App.Views;

/// <summary>Asked before every wire log starts (#139). Cancel is the default and Escape maps to it; closing with
/// the title bar declines too, so the only way to a log is a deliberate click. The point of the modal is the
/// pause: a warning in the user guide is read once, if ever, and never at the moment it matters.</summary>
public partial class WireLogWindow : Window
{
    /// <summary>Design-time only.</summary>
    public WireLogWindow() : this(new WireLogPromptRequest("MVS/CE", "/Users/you/Library/Application Support/LizTerm/logs")) { }

    public WireLogWindow(WireLogPromptRequest request)
    {
        InitializeComponent();
        PathText.Text = request.Directory;
    }

    public bool Confirmed { get; private set; }

    private void OnStartClick(object? sender, RoutedEventArgs e) => Finish(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Finish(false);

    private void Finish(bool confirmed)
    {
        Confirmed = confirmed;
        Close(confirmed);
    }
}
