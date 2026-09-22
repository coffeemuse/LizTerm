// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>Long lists arrive a page at a time (#144). Both lists ask for <see cref="PageSize"/> entries and offer
/// Load more while the host has more. The member filter narrows the list on the browser's side while the whole
/// library is loaded; once the host has more members than are shown, the filter is sent to the host as a pattern
/// instead, so the rows shown are always every match.</summary>
public sealed partial class MvsmfBrowserViewModel
{
    internal int PageSize { get; set; } = 500;

    /// <summary>How long typing in the member filter has to pause before a host-side listing runs.</summary>
    internal TimeSpan FilterDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    private string? _listedPattern;
    private string? _datasetContinuation;
    private string? _memberContinuation;
    private string? _memberPattern;
    /// <summary>True once an unfiltered member listing came back complete: the filter can then narrow it here.</summary>
    private bool _allMembersLoaded;
    private CancellationTokenSource? _filterDebounce;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DatasetsFooter))]
    private bool _hasMoreDatasets;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MembersFooter))]
    private bool _hasMoreMembers;

    /// <summary>True from a keystroke in the member filter until the host-side listing it schedules has started.</summary>
    [ObservableProperty] private bool _isFilterPending;

    partial void OnHasMoreDatasetsChanged(bool value) => NotifyCommands();

    partial void OnHasMoreMembersChanged(bool value) => NotifyCommands();

    private bool CanLoadMoreDatasets => !IsBusy && !IsReviewingUpload && !IsCreating && HasMoreDatasets;
    private bool CanLoadMoreMembers => !IsBusy && !IsReviewingUpload && !IsCreating && HasMoreMembers && SelectedDataset is { IsPartitioned: true };

    [RelayCommand(CanExecute = nameof(CanLoadMoreDatasets))]
    private Task LoadMoreDatasetsAsync() => RunExclusiveAsync(LoadMoreDatasetsCoreAsync, () => LoadMoreDatasetsAsync());

    private async Task LoadMoreDatasetsCoreAsync(CancellationToken token)
    {
        if (_listedPattern is not { } pattern || _datasetContinuation is not { } after) return;
        StatusText = $"⟳ Listing more of {pattern}…";
        var listing = await _connection.RunAsync(service => service.ListDatasetsAsync(pattern, new HostListRequest(PageSize, after), token));
        foreach (var entry in listing.Entries) Datasets.Add(new DatasetRow(entry));
        _datasetContinuation = listing.Continuation;
        HasMoreDatasets = !listing.IsComplete;
        OnPropertyChanged(nameof(DatasetsFooter));
        StatusText = DatasetsStatus();
    }

    private string DatasetsStatus() => Plural(Datasets.Count, "dataset") + (HasMoreDatasets ? " shown, more on the host" : "");

    /// <summary>Lists the filter again and keeps the chosen dataset when it is still listed, reloading its members
    /// and keeping the transfer mode (pane-pattern spec §4.1). Off whenever List is.</summary>
    [RelayCommand(CanExecute = nameof(CanChooseDataset))]
    private Task RefreshAsync() => RunExclusiveAsync(RefreshCoreAsync, () => RefreshAsync());

    private async Task RefreshCoreAsync(CancellationToken token)
    {
        if (SelectedDataset is not { } kept)
        {
            await ListCoreAsync(token);
            return;
        }
        var mode = Mode;
        var (refused, row) = await ListAndSelectAsync(kept.Name, token);
        if (refused) return;
        if (row is null)
        {
            StatusText = HasMoreDatasets
                ? $"⚠ {kept.Name} is not on the first page of {_listedPattern}; load more datasets or narrow the filter. {DatasetsStatus()}"
                : $"⚠ {kept.Name} is no longer listed. {DatasetsStatus()}";
        }
        // Choosing the row again applied the dataset's own choice of mode; the user's choice stands while the record
        // format that choice was made for is unchanged.
        else if (row.Attributes.RecordFormat == kept.Attributes.RecordFormat) Mode = mode;
    }

    [RelayCommand(CanExecute = nameof(CanLoadMoreMembers))]
    private Task LoadMoreMembersAsync()
    {
        if (SelectedDataset is not { } row) return Task.CompletedTask;
        return RunExclusiveAsync(token => LoadMoreMembersCoreAsync(row, token), () => LoadMoreMembersAsync());
    }

    private async Task LoadMoreMembersCoreAsync(DatasetRow row, CancellationToken token)
    {
        if (_memberContinuation is not { } after) return;
        StatusText = $"⟳ Listing more members of {row.Name}…";
        var listing = await _connection.RunAsync(service => service.ListMembersAsync(row.Path, new HostListRequest(PageSize, after, _memberPattern), token));
        if (!ReferenceEquals(SelectedDataset, row)) return;
        AddMembers(row, listing.Entries);
        _memberContinuation = listing.Continuation;
        HasMoreMembers = !listing.IsComplete;
        RefreshVisibleMembers();
        StatusText = MembersStatus();
    }

    /// <summary>The first page of <paramref name="row"/>'s members, unfiltered; then, if the host has more and a
    /// filter is typed, the first page of the host's matches instead.</summary>
    private async Task LoadMembersCoreAsync(DatasetRow row, CancellationToken token)
    {
        await LoadMemberPageAsync(row, null, token);
        if (ReferenceEquals(SelectedDataset, row) && !_allMembersLoaded && TryHostPattern(out var pattern) && pattern is not null)
        {
            // A keystroke that arrived during the first page is applied here; its own listing would only repeat this one.
            CancelHostFilter();
            await LoadMemberPageAsync(row, pattern, token);
        }
    }

    private async Task LoadMemberPageAsync(DatasetRow row, string? pattern, CancellationToken token)
    {
        StatusText = $"⟳ Listing members of {row.Name}…";
        var listing = await _connection.RunAsync(service => service.ListMembersAsync(row.Path, new HostListRequest(PageSize, null, pattern), token));
        if (!ReferenceEquals(SelectedDataset, row)) return;
        ClearMembers();
        AddMembers(row, listing.Entries);
        _memberPattern = pattern;
        _memberContinuation = listing.Continuation;
        if (pattern is null && listing.IsComplete) _allMembersLoaded = true;
        HasMoreMembers = !listing.IsComplete;
        RefreshVisibleMembers();
        StatusText = MembersStatus();
    }

    private void AddMembers(DatasetRow row, IEnumerable<HostFileEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (HostPath.MemberNameError(entry.Name) is null) Members.Add(new MemberRow(row.Name, entry.Name));
        }
    }

    /// <summary>Every member name in the library, asked of the host in one answer, for a check that must not miss
    /// a member on a page that is not loaded.</summary>
    private async Task<HashSet<string>> ListAllMemberNamesAsync(DatasetRow dataset, CancellationToken token)
    {
        StatusText = $"⟳ Listing members of {dataset.Name}…";
        var listing = await _connection.RunAsync(service => service.ListMembersAsync(dataset.Path, HostListRequest.All, token));
        return listing.Entries.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The filter as a host pattern, <c>*TEXT*</c>, or null for an empty filter. False, with the reason on
    /// the status line, for text the host cannot take.</summary>
    private bool TryHostPattern(out string? pattern)
    {
        pattern = null;
        var text = MemberFilter.Trim();
        if (text.Length == 0) return true;
        var candidate = $"*{text.ToUpperInvariant()}*";
        if (HostPath.MemberPatternError(candidate) is { } problem)
        {
            StatusText = "✗ " + problem;
            return false;
        }
        pattern = candidate;
        return true;
    }

    /// <summary>A keystroke in the filter while the host has more members than are shown: after a pause, list the
    /// host's matches. Another keystroke restarts the pause; a listing already running is waited out.</summary>
    private void ScheduleHostFilter()
    {
        CancelHostFilter();
        var debounce = _filterDebounce = new CancellationTokenSource();
        IsFilterPending = true;
        _ = RunHostFilterAsync(debounce);
    }

    private void CancelHostFilter()
    {
        _filterDebounce?.Cancel();
        _filterDebounce = null;
        IsFilterPending = false;
    }

    private async Task RunHostFilterAsync(CancellationTokenSource debounce)
    {
        try
        {
            await Task.Delay(FilterDelay, debounce.Token);
            if (Ops.WhenIdle() is { } idle) await idle.WaitAsync(debounce.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(_filterDebounce, debounce)) CancelHostFilter();
        }
        if (Ops.IsDisposed || SelectedDataset is not { IsPartitioned: true } row || _allMembersLoaded) return;
        if (!TryHostPattern(out var pattern)) return;
        // The operation waited out may have listed this very pattern (a dataset's own load applies the filter).
        if (pattern == _memberPattern) return;
        await RunExclusiveAsync(token => LoadMemberPageAsync(row, pattern, token), () => LoadMembersAsync(SelectedDataset));
    }
}
