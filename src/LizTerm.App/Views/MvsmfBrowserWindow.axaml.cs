// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    /// <summary>A question takes the keyboard to its Cancel button, the safe answer, or to its text box when it has
    /// one, once the strip has been laid out; a focus request on a control that is still hidden is refused. An
    /// operation disables the lists and the filter box, which drops their focus and does not give it back when they
    /// are enabled again, so the window remembers where the keyboard was when the operation started and returns it
    /// there afterwards.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_watched is not { } vm) return;
        switch (e.PropertyName)
        {
            // Raised before IsIdle and CanChooseDataset, whose bindings disable the controls that hold the focus.
            case nameof(MvsmfBrowserViewModel.IsBusy) when vm.IsBusy:
                RememberFocus();
                break;
            case nameof(MvsmfBrowserViewModel.IsBusy) when !vm.HasConfirmation:
                PostRestoreFocus(forget: true);
                break;
            case nameof(MvsmfBrowserViewModel.HasConfirmation) when vm.HasConfirmation:
                // An input question takes the keyboard to its box, with the old name selected so typing replaces it;
                // any other to Cancel, the safe answer.
                Dispatcher.UIThread.Post(() =>
                {
                    if (_watched is not { HasConfirmation: true, Confirmation: { } question }) return;
                    if (question.HasInput)
                    {
                        ConfirmInputBox.Focus();
                        ConfirmInputBox.SelectAll();
                    }
                    else ConfirmCancelButton.Focus();
                }, DispatcherPriority.Loaded);
                break;
            case nameof(MvsmfBrowserViewModel.HasConfirmation):
                PostRestoreFocus(forget: false);
                break;
        }
    }

    private Control? _focusBefore;
    private object? _focusedItemBefore;
    private int _focusedIndexBefore = -1;

    private void RememberFocus()
    {
        _focusBefore = null;
        _focusedItemBefore = null;
        _focusedIndexBefore = -1;
        if (FilterBox.IsKeyboardFocusWithin) _focusBefore = FilterBox;
        else if (FocusedList() is { } list)
        {
            _focusBefore = list;
            if (FocusManager?.GetFocusedElement() is Control focused
                && focused.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { } container)
            {
                _focusedItemBefore = list.ItemFromContainer(container);
                _focusedIndexBefore = list.IndexFromContainer(container);
            }
        }
    }

    private ListBox? FocusedList() =>
        DatasetList.IsKeyboardFocusWithin ? DatasetList : MemberList.IsKeyboardFocusWithin ? MemberList : null;

    /// <summary>At Loaded priority, so a list refilled by the operation has its containers. Only when the keyboard
    /// has nowhere better to be: the user may have moved it on meanwhile. A question that closes while its operation
    /// still runs tries too, but keeps the memory for the end of the operation, when the controls are enabled.</summary>
    private void PostRestoreFocus(bool forget)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_focusBefore is not { } target || _watched is not { HasConfirmation: false } vm) return;
            if (forget && vm.IsBusy) return;
            if (FocusManager?.GetFocusedElement() is Control { IsEffectivelyVisible: true, IsEffectivelyEnabled: true }) return;
            var (item, index) = (_focusedItemBefore, _focusedIndexBefore);
            if (forget)
            {
                _focusBefore = null;
                _focusedItemBefore = null;
                _focusedIndexBefore = -1;
            }
            if (target is ListBox list) FocusRow(list, item, index);
            else target.Focus();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>The row that had the focus if it is still listed, else the selected row, else the row now at its
    /// place (a deleted member's neighbour). A list box itself does not take the focus.</summary>
    private static void FocusRow(ListBox list, object? item, int index)
    {
        if (item is not null && list.ContainerFromItem(item)?.Focus() == true) return;
        if (list.SelectedItem is { } selected && list.ContainerFromItem(selected)?.Focus() == true) return;
        if (index >= 0 && list.ItemCount > 0) list.ContainerFromIndex(Math.Min(index, list.ItemCount - 1))?.Focus();
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
        _watched = null;
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
                else if (vm.IsReviewingUpload) vm.CloseReviewCommand.Execute(null);
                else Close();
                break;
            case Key.Enter when vm.Confirmation is { HasInput: true } inputQuestion && ConfirmInputBox.IsKeyboardFocusWithin:
                e.Handled = true;
                if (inputQuestion.PrimaryCommand.CanExecute(null)) inputQuestion.PrimaryCommand.Execute(null);
                break;
            case Key.Enter when FilterBox.IsFocused:
                e.Handled = true;
                if (vm.ListCommand.CanExecute(null)) _ = vm.ListCommand.ExecuteAsync(null);
                break;
            case Key.Enter when MemberList.IsKeyboardFocusWithin:
                e.Handled = true;
                if (vm.DownloadCommand.CanExecute(null)) _ = vm.DownloadCommand.ExecuteAsync(null);
                break;
            // Backspace is the key Apple labels "delete".
            case Key.Delete or Key.Back when MemberList.IsKeyboardFocusWithin:
                e.Handled = true;
                if (vm.DeleteCommand.CanExecute(null)) _ = vm.DeleteCommand.ExecuteAsync(null);
                break;
        }
    }
}
