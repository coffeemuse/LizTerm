// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Views;

/// <summary>The mvsMF Browser (spec §4). Owned by its session window and shown with ShowAbove; it never refuses to
/// close — closing cancels what runs and releases the connection.</summary>
public partial class MvsmfBrowserWindow : Window
{
    public MvsmfBrowserWindow()
    {
        InitializeComponent();
        MemberList.SelectionChanged += (_, _) => PushSelectedMembers();
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            FilterBox.Focus();
            if (ViewModel is { Filter.Length: > 0 } vm && vm.Datasets.Count == 0) _ = vm.ListCommand.ExecuteAsync(null);
        };
    }

    private MvsmfBrowserViewModel? _watched;

    private MvsmfBrowserViewModel? ViewModel => DataContext as MvsmfBrowserViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_watched is not null) _watched.PropertyChanged -= OnViewModelPropertyChanged;
        _watched = ViewModel;
        if (_watched is not null) _watched.PropertyChanged += OnViewModelPropertyChanged;
        base.OnDataContextChanged(e);
    }

    /// <summary>A question takes the keyboard to its Cancel button, the safe answer, once the strip has been laid
    /// out; a focus request on a control that is still hidden is refused.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MvsmfBrowserViewModel.HasConfirmation) || _watched is not { HasConfirmation: true }) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_watched is { HasConfirmation: true }) ConfirmCancelButton.Focus();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Only rows the member filter still shows: a transfer or a delete must never act on a member the user
    /// cannot see. The list box drops hidden rows from its selection when the filter refills the list, and this keeps
    /// that true whatever order the two happen in.</summary>
    private void PushSelectedMembers()
    {
        if (ViewModel is not { } vm) return;
        var visible = vm.VisibleMembers.ToHashSet();
        vm.SetSelectedMembers(MemberList.SelectedItems?.OfType<MemberRow>().Where(visible.Contains) ?? []);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_watched is not null) _watched.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel?.Dispose();
        base.OnClosed(e);
    }

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                if (vm.Confirmation is { } question) question.CancelCommand.Execute(null);
                else if (vm.IsBusy) vm.CancelCommand.Execute(null);
                else Close();
                break;
            case Key.Enter when FilterBox.IsFocused:
                e.Handled = true;
                if (vm.ListCommand.CanExecute(null)) _ = vm.ListCommand.ExecuteAsync(null);
                break;
            case Key.Enter when MemberList.IsKeyboardFocusWithin:
                e.Handled = true;
                if (vm.DownloadCommand.CanExecute(null)) _ = vm.DownloadCommand.ExecuteAsync(null);
                break;
            case Key.Delete when MemberList.IsKeyboardFocusWithin:
                e.Handled = true;
                if (vm.DeleteCommand.CanExecute(null)) _ = vm.DeleteCommand.ExecuteAsync(null);
                break;
        }
    }
}
