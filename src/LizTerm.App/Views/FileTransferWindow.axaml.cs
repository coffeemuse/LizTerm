// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Views;

/// <summary>The File Transfer dialog. The caller sets DataContext to a <see cref="FileTransferViewModel"/> (from
/// <see cref="SessionViewModel.CreateTransfer"/>); the three panels switch on its phase.</summary>
public partial class FileTransferWindow : Window
{
    public FileTransferWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private FileTransferViewModel? ViewModel => DataContext as FileTransferViewModel;

    /// <summary>Whether a close of this dialog, or of the session window that owns it, must be refused now: a
    /// running transfer is asked to cancel and the window kept until the Done panel shows the outcome; closing
    /// again while that answer is still pending lets the window go. The policy is
    /// <see cref="FileTransferViewModel.TryClose"/>. The owner asks from its own OnClosing, after its close
    /// questions (#151), because its ClosingBehavior is OwnerWindowOnly rather than Avalonia's child-first default.</summary>
    internal bool RefusesClose() => ViewModel is { } vm && !vm.TryClose();

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (RefusesClose()) e.Cancel = true;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Escape closes the dialog in every phase, through Closing and so through TryClose: on a running
    /// transfer the first Escape cancels and the window stays until the outcome shows, exactly like Close. Handled
    /// here rather than with IsCancel on a button because the Running panel has no Close button. While the cancel
    /// is unanswered Escape does nothing more: a held key auto-repeats, and a repeat must not become the deliberate
    /// second close that lets the window go before the outcome shows (the title bar still can).</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled && ViewModel is not { IsRunning: true, IsCancelling: true })
        {
            e.Handled = true;
            Close();
            return;
        }
        base.OnKeyDown(e);
    }
}
