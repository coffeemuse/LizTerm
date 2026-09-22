// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>View (viewer spec §3, §4): one member, or a sequential dataset, read as text and handed to the
/// viewer window. Always Text, whatever the transfer drop-down says — viewing is reading a document — and the
/// transfer options contribute only the trimming.</summary>
public sealed partial class MvsmfBrowserViewModel
{
    /// <summary>The last View's state, null until one has been read. The window opens its viewer on the first one
    /// and reuses that window for each later one; a failed or cancelled read sets none.</summary>
    [ObservableProperty] private MvsmfViewerViewModel? _viewer;

    /// <summary>How this platform writes the View gesture (⌘⏎ or Ctrl+Enter). The window sets it once from the
    /// platform's hotkey configuration; the view model has no way to ask, and its tests pass their own.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewHint))]
    private string _viewGestureText = "";

    /// <summary>Exactly one member, unlike Download, which takes many: a viewer shows one document. A dataset
    /// whose records are undefined-length is a load library, and its members are object code.</summary>
    private bool CanView =>
        !IsBusy && !IsCreating && SelectedDataset is { IsSupported: true } dataset
        && dataset.Attributes.RecordFormat != RecordFormatFamily.Undefined
        && (dataset.IsSequential || _selectedMembers.Count == 1);

    /// <summary>The View button's tooltip, shown on the disabled button too (ToolTip.ShowOnDisabled), so the one
    /// state the user cannot work out from the selection says why it is off.</summary>
    public string ViewHint
    {
        get
        {
            if (SelectedDataset is { IsSupported: true, Attributes.RecordFormat: RecordFormatFamily.Undefined } notText)
                return $"{notText.Name} holds undefined-length records, which are not text.";
            var verb = SelectedDataset is { IsSequential: true } ? "View this dataset" : "View the selected member";
            return ViewGestureText.Length > 0 ? $"{verb} ({ViewGestureText})" : verb;
        }
    }

    [RelayCommand(CanExecute = nameof(CanView))]
    private Task ViewAsync() => RunExclusiveAsync(ViewCoreAsync, () => ViewAsync());

    private async Task ViewCoreAsync(CancellationToken token)
    {
        var dataset = SelectedDataset!;
        HostPath path;
        if (dataset.IsSequential) path = dataset.Path;
        // The retry set by a failed View can outlive the selection it was built for: a filter keystroke can narrow
        // the member list to nothing while a failure's banner is still up, and Retry must not index an empty list.
        else if (_selectedMembers is [var only]) path = only.Path;
        else return;
        var progress = new RowProgress(_dispatch, bytes => StatusText = $"⟳ Reading {path} · {Bytes(bytes)} bytes");
        StatusText = $"⟳ Reading {path}…";
        // withEtag: false — a view can never write the content back, and a stamp costs the host a second pass over
        // it (browser spec §5.2). The stamp memory is left exactly as it was: a view is not a download.
        HostTextRead read;
        try
        {
            read = await _connection.RunAsync(service => service.ReadTextAsync(path, progress, withEtag: false, token));
        }
        finally
        {
            // A cancelled or failed read must not leave the status line reading a stale "⟳ Reading …": RowProgress
            // exists precisely so a report still in the queue cannot overwrite the outcome RunExclusiveAsync writes
            // next, and that only holds if Close runs on every path, not just the success one (Downloads.cs does).
            progress.Close();
        }
        // Progress and the result go on the status line, never on the member's row: a row saying "Done · n bytes"
        // after a view would read like a transfer that happened. The result is written first because setting
        // Viewer opens the window synchronously, and a window that cannot be shown puts its own message here
        // (MvsmfBrowserWindow.ShowViewer); a "✓ Read …" written afterwards would overwrite that failure.
        StatusText = $"✓ Read {path} · {Plural(read.Lines.Count, "line")}.";
        Viewer = new MvsmfViewerViewModel(path.ToString(), read.Lines, TrimTrailingBlanks);
    }
}
