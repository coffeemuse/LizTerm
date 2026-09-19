// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.Controls;

namespace LizTerm.App.Views;

/// <summary>The Preferences window's Keyboard tab (editable keymap spec §5.3). Layout: the host sets the data
/// context, a <see cref="ViewModels.KeymapEditorViewModel"/>, and everything is binding but one click.</summary>
public partial class KeyboardTab : UserControl
{
    public KeyboardTab() => InitializeComponent();

    /// <summary>A row's Cancel button acts on a control, the slot beside it, which no view model holds: it is found
    /// through the panel the two share. The focus goes back to the slot, whichever control the press moved it to.</summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control cancel || cancel.Parent is not Panel row) return;
        var slot = row.Children.OfType<ChordCaptureBox>().Single();
        slot.Cancel();
        slot.Focus();
    }
}
