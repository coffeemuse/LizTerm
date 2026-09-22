// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LizTerm.App.Dialogs;
using LizTerm.App.ViewModels;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Views;

/// <summary>mvsMF Access (browser spec §4, pane-pattern spec §4): two BrowserPanes, each with the verbs that act on
/// its own selection, over a window-level status line. Owned by its session window and shown with ShowAbove; it
/// never refuses to close — closing cancels what runs and releases the connection.</summary>
public partial class MvsmfBrowserWindow : Window
{
    /// <summary>Cmd+R on macOS, Ctrl+R elsewhere: the platform's command modifier, as the session window's Find.</summary>
    internal KeyGesture RefreshGesture { get; private set; } = new(Key.R, KeyModifiers.Control);

    /// <summary>Cmd+N on macOS, Ctrl+N elsewhere.</summary>
    internal KeyGesture NewDatasetGesture { get; private set; } = new(Key.N, KeyModifiers.Control);

    /// <summary>Cmd+Enter on macOS, Ctrl+Enter elsewhere. Plain Enter stays Download, so this case is matched
    /// first in the key tunnel, and it is taken from whichever list owns the target: the member list for a
    /// member, the dataset list for a sequential dataset, whose Members pane is not shown at all.</summary>
    internal KeyGesture ViewGesture { get; private set; } = new(Key.Enter, KeyModifiers.Control);

    public MvsmfBrowserWindow()
    {
        InitializeComponent();
        // The owned New dataset dialog's own OnClosing must never veto this window's close (it never refuses to
        // close): Avalonia's default child-first ClosingBehavior would ask the dialog ahead of this window's own
        // OnClosing/OnClosed, and the dialog cancels while a create is in flight. OwnerWindowOnly, SessionWindow's
        // and FileTransferWindow's rule, closes the dialog with the window instead of asking it first.
        ClosingBehavior = WindowClosingBehavior.OwnerWindowOnly;
        MemberList.SelectionChanged += (_, _) => PushSelectedMembers();
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);

        // The platform's command modifier (Cmd on macOS, Ctrl elsewhere), as SessionWindow.ShowPlatformGestures.
        var modifiers = this.GetPlatformSettings()?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        RefreshGesture = new KeyGesture(Key.R, modifiers);
        NewDatasetGesture = new KeyGesture(Key.N, modifiers);
        ViewGesture = new KeyGesture(Key.Enter, modifiers);
        DatasetMenuRefresh.InputGesture = RefreshGesture;
        DatasetMenuNew.InputGesture = NewDatasetGesture;
        MemberMenuView.InputGesture = ViewGesture;
        ToolTip.SetTip(RefreshButton, $"Refresh the list ({RefreshGesture.ToString("p", null)})");
        ToolTip.SetTip(NewDatasetButton, $"Allocate a new dataset ({NewDatasetGesture.ToString("p", null)})");
        MemberList.AddHandler(InputElement.DoubleTappedEvent, OnMemberDoubleTapped);
        FileList.SelectionChanged += (_, _) => PushSelectedFiles();
        DirectoryList.AddHandler(InputElement.DoubleTappedEvent, OnDirectoryDoubleTapped);
        FileList.AddHandler(InputElement.DoubleTappedEvent, OnFileDoubleTapped);
        DirectoryMenuRefresh.InputGesture = RefreshGesture;
        DirectoryMenuNew.InputGesture = NewDatasetGesture;
        FileMenuView.InputGesture = ViewGesture;
        ToolTip.SetTip(UssRefreshButton, $"List this directory again ({RefreshGesture.ToString("p", null)})");
        ToolTip.SetTip(NewDirectoryButton, $"Create a directory here ({NewDatasetGesture.ToString("p", null)})");
        Tabs.SelectionChanged += OnTabChanged;

        Opened += (_, _) =>
        {
            FilterBox.Focus();
            if (ViewModel is { Filter.Length: > 0 } vm && vm.Datasets.Count == 0) _ = vm.ListCommand.ExecuteAsync(null);
        };
    }

    /// <summary>A double-click on a member row is Download, as Enter is. On a row only: a double-click on the empty
    /// part of the list selects nothing and must download nothing.</summary>
    private void OnMemberDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is null) return;
        if (vm.DownloadCommand.CanExecute(null)) _ = vm.DownloadCommand.ExecuteAsync(null);
    }

    /// <summary>Whether the USS tab is in front: the keys and the focus fallback go to its controls then.</summary>
    internal bool IsUssTab => ReferenceEquals(Tabs.SelectedItem, UssTab);

    /// <summary>The USS tab lists its start path the first time it is shown, never at window open (USS spec §4.5).</summary>
    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!IsUssTab || ViewModel is not { } vm) return;
        _ = vm.Uss.EnsureListedAsync();
    }

    /// <summary>A double-click on a directory row opens it, as Enter does; on a file row it downloads. On a row
    /// only: a double-click on the empty part of a list selects nothing and must do nothing.</summary>
    private void OnDirectoryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is null) return;
        if (vm.Uss.OpenDirectoryCommand.CanExecute(null)) _ = vm.Uss.OpenDirectoryCommand.ExecuteAsync(null);
    }

    private void OnFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is null) return;
        if (vm.Uss.DownloadCommand.CanExecute(null)) _ = vm.Uss.DownloadCommand.ExecuteAsync(null);
    }

    /// <summary>The file list is the selection's owner, as the member list is (PushSelectedMembers).</summary>
    private void PushSelectedFiles()
    {
        if (ViewModel is not { } vm) return;
        vm.Uss.SetSelectedFiles(FileList.SelectedItems?.OfType<FileRow>() ?? []);
    }

    /// <summary>A refresh keeping the selection: the list box takes the rows and pushes them back.</summary>
    private void SelectFiles(IReadOnlyList<FileRow> rows)
    {
        if (FileList.SelectedItems is not { } selected) return;
        selected.Clear();
        foreach (var row in rows) selected.Add(row);
    }

    private void OnUssPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_watched is not { } vm) return;
        if (e.PropertyName == nameof(UssBrowserViewModel.Viewer) && vm.Uss.Viewer is { } viewer) ShowViewer(viewer);
    }

    /// <summary>The USS tab's keys (USS spec §4.5), mirroring the Datasets tab's: handled only while it is in
    /// front, and only the keys it owns; Escape's ladder is shared and stays in the caller.</summary>
    private bool HandleUssKey(MvsmfBrowserViewModel vm, KeyEventArgs e)
    {
        var uss = vm.Uss;
        switch (e.Key)
        {
            case Key.R when e.KeyModifiers == RefreshGesture.KeyModifiers:
                if (uss.RefreshCommand.CanExecute(null)) _ = uss.RefreshCommand.ExecuteAsync(null);
                return true;
            case Key.N when e.KeyModifiers == NewDatasetGesture.KeyModifiers:
                if (uss.NewDirectoryCommand.CanExecute(null)) _ = uss.NewDirectoryCommand.ExecuteAsync(null);
                return true;
            case Key.Delete or Key.Back when DirectoryList.IsKeyboardFocusWithin:
                if (uss.DeleteDirectoryCommand.CanExecute(null)) _ = uss.DeleteDirectoryCommand.ExecuteAsync(null);
                return true;
            case Key.Delete or Key.Back when FileList.IsKeyboardFocusWithin:
                if (uss.DeleteFilesCommand.CanExecute(null)) _ = uss.DeleteFilesCommand.ExecuteAsync(null);
                return true;
            case Key.Enter when e.KeyModifiers == ViewGesture.KeyModifiers && FileList.IsKeyboardFocusWithin:
                if (uss.ViewCommand.CanExecute(null)) _ = uss.ViewCommand.ExecuteAsync(null);
                return true;
            case Key.Enter when e.KeyModifiers == KeyModifiers.None && PathBox.IsFocused:
                if (uss.GoCommand.CanExecute(null)) _ = uss.GoCommand.ExecuteAsync(null);
                return true;
            case Key.Enter when e.KeyModifiers == KeyModifiers.None && DirectoryList.IsKeyboardFocusWithin:
                if (uss.OpenDirectoryCommand.CanExecute(null)) _ = uss.OpenDirectoryCommand.ExecuteAsync(null);
                return true;
            case Key.Enter when e.KeyModifiers == KeyModifiers.None && FileList.IsKeyboardFocusWithin:
                if (uss.DownloadCommand.CanExecute(null)) _ = uss.DownloadCommand.ExecuteAsync(null);
                return true;
            default:
                return false;
        }
    }

    private MvsmfBrowserViewModel? _watched;

    private MvsmfBrowserViewModel? ViewModel => DataContext as MvsmfBrowserViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnViewModelPropertyChanged;
            _watched.SelectMemberRequested -= SelectMember;
            _watched.Uss.PropertyChanged -= OnUssPropertyChanged;
            _watched.Uss.SelectFilesRequested -= SelectFiles;
        }
        _watched = ViewModel;
        if (_watched is not null)
        {
            _watched.PropertyChanged += OnViewModelPropertyChanged;
            _watched.SelectMemberRequested += SelectMember;
            _watched.ViewGestureText = ViewGesture.ToString("p", null);
            _watched.Uss.PropertyChanged += OnUssPropertyChanged;
            _watched.Uss.SelectFilesRequested += SelectFiles;
            _watched.Uss.ViewGestureText = ViewGesture.ToString("p", null);
        }
        base.OnDataContextChanged(e);
    }

    /// <summary>The list box is the selection's owner; setting its SelectedItem replaces the selection with the one
    /// row, and its SelectionChanged pushes that back to the view model.</summary>
    private void SelectMember(MemberRow row) => MemberList.SelectedItem = row;

    /// <summary>A question takes the keyboard to its Cancel button, the safe answer, or to its text box when it has
    /// one, once the strip has been laid out; a focus request on a control that is still hidden is refused. An
    /// operation disables the lists and the filter box, which drops their focus and does not give it back when they
    /// are enabled again, so the window remembers where the keyboard was — the filter box, a list's row, or any
    /// other control, the form's boxes included — and returns it there afterwards.</summary>
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
                // The USS tab's own listing (OnTabChanged) is a no-op while an operation on the shared runner is
                // still busy, so a tab selected during the window's own opening listing never lists on its own;
                // EnsureListedAsync is idempotent once listed, so this is a no-op after the tab's own listings.
                if (IsUssTab) _ = vm.Uss.EnsureListedAsync();
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
            case nameof(MvsmfBrowserViewModel.IsCreating) when vm.IsCreating:
                _ = ShowNewDatasetAsync(vm);
                break;
            case nameof(MvsmfBrowserViewModel.IsCreating) when !vm.IsBusy:
                // Closed without an operation (Cancel, Escape, the close box): the dialog gave the keyboard back to
                // this window, which puts it where the window opens. A form an operation closes is handled by the
                // IsBusy case above.
                Dispatcher.UIThread.Post(() =>
                {
                    if (_watched is not { IsCreating: false, IsBusy: false }) return;
                    if (FocusManager?.GetFocusedElement() is Control { IsEffectivelyVisible: true }) return;
                    (IsUssTab ? PathBox : FilterBox).Focus();
                }, DispatcherPriority.Loaded);
                break;
            case nameof(MvsmfBrowserViewModel.Viewer) when vm.Viewer is { } viewer:
                ShowViewer(viewer);
                break;
        }
    }

    /// <summary>The open New dataset dialog, if any (pane-pattern spec §6); for the tests and the close path.</summary>
    internal NewDatasetWindow? NewDatasetDialog { get; private set; }

    /// <summary>Opens the form as a modal dialog over this window. The dialog closes itself when IsCreating turns
    /// off. A dialog that cannot be shown is a status line, and the form is closed so the commands come back.</summary>
    private async Task ShowNewDatasetAsync(MvsmfBrowserViewModel vm)
    {
        if (NewDatasetDialog is not null) return;
        var dialog = new NewDatasetWindow { DataContext = vm };
        NewDatasetDialog = dialog;
        // Freed on Closed, which Close raises at once: the await below resumes a turn later, and a form closed and
        // opened again within one turn must find the slot free, and must not have its own dialog cleared by the
        // finally of the one before.
        dialog.Closed += (_, _) => { if (ReferenceEquals(NewDatasetDialog, dialog)) NewDatasetDialog = null; };
        try
        {
            await dialog.ShowDialogAbove(this);
        }
        catch (Exception ex)
        {
            vm.StatusText = "✗ Could not open the New dataset window: " + ex.Message;
            if (vm.CloseFormCommand.CanExecute(null)) vm.CloseFormCommand.Execute(null);
        }
        finally
        {
            if (ReferenceEquals(NewDatasetDialog, dialog)) NewDatasetDialog = null;
        }
    }

    /// <summary>The open text viewer, if any (viewer spec §6.1); for the tests and for reuse.</summary>
    internal MvsmfViewerWindow? ViewerWindow { get; private set; }

    /// <summary>One viewer per browser window: a later View replaces its contents and fronts it. Owned and
    /// non-blocking, so the browser stays usable behind it and the viewer closes with it. A window that cannot be
    /// shown is a status line, as the New dataset dialog is.</summary>
    private void ShowViewer(MvsmfViewerViewModel viewer)
    {
        if (ViewerWindow is { } open)
        {
            open.DataContext = viewer;
            open.Activate();
            return;
        }
        var window = new MvsmfViewerWindow { DataContext = viewer };
        ViewerWindow = window;
        window.Closed += (_, _) => { if (ReferenceEquals(ViewerWindow, window)) ViewerWindow = null; };
        try
        {
            window.ShowAbove(this);
        }
        catch (Exception ex)
        {
            ViewerWindow = null;
            if (_watched is { } vm) vm.StatusText = "✗ Could not open the viewer window: " + ex.Message;
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
        if (FocusedList() is { } list)
        {
            _focusBefore = list;
            if (FocusManager?.GetFocusedElement() is Control focused
                && focused.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { } container)
            {
                _focusedItemBefore = list.ItemFromContainer(container);
                _focusedIndexBefore = list.IndexFromContainer(container);
            }
        }
        else if (FocusManager?.GetFocusedElement() is Control other) _focusBefore = other;
    }

    private ListBox? FocusedList() =>
        DatasetList.IsKeyboardFocusWithin ? DatasetList
        : MemberList.IsKeyboardFocusWithin ? MemberList
        : DirectoryList.IsKeyboardFocusWithin ? DirectoryList
        : FileList.IsKeyboardFocusWithin ? FileList
        : null;

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
            // The pane it was in may have closed (the form after a Create); the filter box is where the window opens.
            if (!target.IsEffectivelyVisible) target = IsUssTab ? PathBox : FilterBox;
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
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnViewModelPropertyChanged;
            _watched.SelectMemberRequested -= SelectMember;
            _watched.Uss.PropertyChanged -= OnUssPropertyChanged;
            _watched.Uss.SelectFilesRequested -= SelectFiles;
        }
        _watched = null;
        ViewModel?.Dispose();
        base.OnClosed(e);
    }

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        // The confirmation strip's Enter and Escape are the window's whichever tab is in front, so they stay below;
        // the USS tab's own keys are taken here first, and a key it does not own falls through to the shared cases.
        if (IsUssTab && vm.Confirmation is null && HandleUssKey(vm, e))
        {
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.R when e.KeyModifiers == RefreshGesture.KeyModifiers:
                e.Handled = true;
                if (vm.RefreshCommand.CanExecute(null)) _ = vm.RefreshCommand.ExecuteAsync(null);
                break;
            case Key.N when e.KeyModifiers == NewDatasetGesture.KeyModifiers:
                e.Handled = true;
                if (vm.NewDatasetCommand.CanExecute(null)) vm.NewDatasetCommand.Execute(null);
                break;
            // The dataset list's own Delete/Backspace; disjoint from the member list's case below because only one
            // list can hold the focus.
            case Key.Delete or Key.Back when DatasetList.IsKeyboardFocusWithin:
                e.Handled = true;
                if (vm.DeleteDatasetCommand.CanExecute(null)) _ = vm.DeleteDatasetCommand.ExecuteAsync(null);
                break;
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
            // The member list for a member; the dataset list for a sequential dataset, whose Members pane — and
            // with it MemberList — is collapsed, so the gesture would otherwise never reach the one verb the
            // button's own tooltip offers there. The dataset list's plain Enter is untouched: it has no case.
            case Key.Enter when e.KeyModifiers == ViewGesture.KeyModifiers
                && (MemberList.IsKeyboardFocusWithin
                    || (DatasetList.IsKeyboardFocusWithin && vm.SelectedDataset is { IsSequential: true })):
                e.Handled = true;
                if (vm.ViewCommand.CanExecute(null)) _ = vm.ViewCommand.ExecuteAsync(null);
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
