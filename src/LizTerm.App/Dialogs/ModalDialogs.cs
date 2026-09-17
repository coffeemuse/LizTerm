// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;

namespace LizTerm.App.Dialogs;

/// <summary>The one way the app opens a modal dialog. On macOS, Keep on Top puts a window at the floating window
/// level, and Avalonia orders an owned dialog only within its own level, so a normal-level dialog draws beneath a
/// Keep on Top owner, which refuses input while the dialog is open. The dialog therefore takes its owner's Topmost
/// and follows it while open, since the macOS menu bar can still flip Keep on Top over a modal dialog.
/// ModalDialogsTests fails for any other ShowDialog call in the app. <see cref="ShowAbove"/> is the same rule for a
/// window that belongs to one session window but does not block it, the mvsMF Browser.</summary>
public static class ModalDialogs
{
    public static Task ShowDialogAbove(this Window dialog, Window owner) => dialog.ShowDialogAbove<object?>(owner);

    public static async Task<TResult> ShowDialogAbove<TResult>(this Window dialog, Window owner)
    {
        void Follow(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.TopmostProperty) dialog.Topmost = owner.Topmost;
        }

        dialog.Topmost = owner.Topmost;
        owner.PropertyChanged += Follow;
        try
        {
            return await dialog.ShowDialog<TResult>(owner);
        }
        finally
        {
            owner.PropertyChanged -= Follow;
        }
    }

    /// <summary>Shows <paramref name="window"/> owned by <paramref name="owner"/> without blocking it. Owned, it
    /// stays above the owner and closes with it; like a dialog, it takes the owner's Keep on Top and follows it
    /// while open, for the reason given on the class.</summary>
    public static void ShowAbove(this Window window, Window owner)
    {
        void Follow(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.TopmostProperty) window.Topmost = owner.Topmost;
        }

        window.Topmost = owner.Topmost;
        // Subscribed only once shown: a Show that throws (a closed owner) must not leave the owner holding a
        // handler that no Closed would ever remove.
        window.Show(owner);
        owner.PropertyChanged += Follow;
        window.Closed += (_, _) => owner.PropertyChanged -= Follow;
    }
}
