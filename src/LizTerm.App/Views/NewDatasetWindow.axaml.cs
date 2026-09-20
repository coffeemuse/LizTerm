// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Views;

/// <summary>The New dataset form as an owned modal dialog (pane-pattern spec §6), bound to the browser's view
/// model so the form, Create and Close are the ones the view model already has. Its life is the view model's
/// IsCreating: the browser window opens it when that turns on, and a create that succeeds, or Close, turns it off
/// and the dialog goes with it. A refused create leaves it on, so the dialog stays with the message. The title
/// bar's close box is the form's Close; while the create runs the form cannot be closed, so neither can the
/// dialog.</summary>
public partial class NewDatasetWindow : Window
{
    private MvsmfBrowserViewModel? _watched;
    private bool _closingWithForm;

    public NewDatasetWindow()
    {
        InitializeComponent();
        Opened += (_, _) => NewNameBox.Focus();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_watched is not null) _watched.PropertyChanged -= OnViewModelPropertyChanged;
        _watched = DataContext as MvsmfBrowserViewModel;
        if (_watched is not null) _watched.PropertyChanged += OnViewModelPropertyChanged;
        base.OnDataContextChanged(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MvsmfBrowserViewModel.IsCreating) || _watched is not { IsCreating: false }) return;
        _closingWithForm = true;
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closingWithForm && _watched is { IsCreating: true } vm)
        {
            // Refused here, then closed through the form: IsCreating turning off comes back as the Close above.
            // Posted, so the view model's notification does not close a window that is still inside Closing.
            e.Cancel = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (vm.CloseFormCommand.CanExecute(null)) vm.CloseFormCommand.Execute(null);
            });
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_watched is not null) _watched.PropertyChanged -= OnViewModelPropertyChanged;
        _watched = null;
        base.OnClosed(e);
    }
}
