// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.Files;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>The mvsMF Browser (spec §4): datasets on the left, members of the chosen PDS on the right, and the
/// transfers in the bottom bar. One operation at a time; see the partial files for downloads, uploads and delete.</summary>
public sealed partial class MvsmfBrowserViewModel : ObservableObject, IDisposable
{
    private readonly HostFileAccess _access;
    private readonly HostFileConnection _connection;
    private readonly IFilePicker _picker;
    private readonly Action<Action> _dispatch;
    private readonly Func<Task>? _openGuide;
    private CancellationTokenSource? _cts;
    private Func<Task>? _retry;
    private List<MemberRow> _selectedMembers = [];
    private bool _disposed;
    private string? _pinSaveWarning;

    public MvsmfBrowserViewModel(HostFileAccess access, HostFileConnection connection, IFilePicker picker,
        Action<Action> dispatch, Func<Task>? openGuide = null)
    {
        _access = access;
        _connection = connection;
        _picker = picker;
        _dispatch = dispatch;
        _openGuide = openGuide;
        _filter = access.Userid is { Length: > 0 } userid ? userid + ".**" : "";
        _access.PinSaveFailed += OnPinSaveFailed;
    }

    /// <summary>The operation that accepted the pin goes on; the warning replaces its status line when it ends.</summary>
    private void OnPinSaveFailed(object? sender, string message) => _dispatch(() =>
    {
        if (_disposed) return;
        if (IsBusy) _pinSaveWarning = message;
        else StatusText = "⚠ " + message;
    });

    public string Title => $"mvsMF Browser — {_access.ProfileName} (Preview)";

    public ObservableCollection<DatasetRow> Datasets { get; } = [];
    public ObservableCollection<MemberRow> Members { get; } = [];
    public ObservableCollection<MemberRow> VisibleMembers { get; } = [];

    [ObservableProperty] private string _filter;
    [ObservableProperty] private string _memberFilter = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMembers), nameof(ShowSequentialNote), nameof(ShowChooseHint), nameof(ChooseHint),
        nameof(MembersHeader), nameof(ShowPaddingNote), nameof(UploadHeader), nameof(ShowMemberPane))]
    private DatasetRow? _selectedDataset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextMode), nameof(IsBinaryMode), nameof(ShowPaddingNote))]
    private HostTransferMode _mode = HostTransferMode.Text;

    [ObservableProperty] private bool _trimTrailingBlanks = true;
    [ObservableProperty] private bool _verifyUploads = true;
    [ObservableProperty] private bool _expandTabs = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(CanChooseDataset))]
    private bool _isBusy;

    [ObservableProperty] private string _statusText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(CanRetry))]
    private string? _errorText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConfirmation))]
    private ConfirmationRequest? _confirmation;

    public bool IsIdle => !IsBusy;
    public bool HasError => ErrorText is not null;
    public bool CanRetry => HasError && _retry is not null;
    public bool HasConfirmation => Confirmation is not null;

    public bool IsTextMode
    {
        get => Mode == HostTransferMode.Text;
        set { if (value) Mode = HostTransferMode.Text; }
    }

    public bool IsBinaryMode
    {
        get => Mode == HostTransferMode.Binary;
        set { if (value) Mode = HostTransferMode.Binary; }
    }

    public bool ShowMembers => SelectedDataset is { IsPartitioned: true };
    public bool ShowSequentialNote => SelectedDataset is { IsSequential: true };
    public bool ShowChooseHint => SelectedDataset is not { IsSupported: true };

    public string ChooseHint => SelectedDataset is { IsSupported: false } dataset
        ? $"{dataset.Name} cannot be opened in this release (DSORG {(dataset.Dsorg.Length > 0 ? dataset.Dsorg : "unknown")})."
        : "Choose a dataset on the left.";

    public string MembersHeader => SelectedDataset is { IsPartitioned: true } dataset
        ? $"{dataset.Name} · {Plural(Members.Count, "member")}{(HasMoreMembers ? " shown, more on the host" : "")}"
        : "";

    /// <summary>Binary transfers to fixed-length records are padded to whole records (compatibility log,
    /// binary-fixed-padding), so the bar says so while it applies.</summary>
    public bool ShowPaddingNote => IsBinaryMode && SelectedDataset?.Attributes.RecordFormat == RecordFormatFamily.Fixed;

    public IReadOnlyList<MemberRow> SelectedMembers => _selectedMembers;

    /// <summary>The window pushes the member list's selection here; a list box's own selected items are not bindable
    /// both ways in a way the tests can drive.</summary>
    public void SetSelectedMembers(IEnumerable<MemberRow> members)
    {
        _selectedMembers = [.. members];
        OnPropertyChanged(nameof(SelectedMembers));
        NotifyCommands();
    }

    partial void OnSelectedDatasetChanged(DatasetRow? value)
    {
        // A pending retry belongs to the dataset it failed on; Retry must never act on another one.
        _retry = null;
        OnPropertyChanged(nameof(CanRetry));
        // A review belongs to the dataset it was opened on; the window disables the list while one is open. Closed
        // before the mode changes, so the old review is not rechecked against the new dataset. A running upload keeps
        // its review, which holds its rows' results.
        if (IsReviewingUpload && !IsBusy) CloseReview();
        if (value is { IsSupported: true }) Mode = value.Attributes.RecordFormat == RecordFormatFamily.Undefined ? HostTransferMode.Binary : HostTransferMode.Text;
        _ = LoadMembersAsync(value);
    }

    partial void OnMemberFilterChanged(string value)
    {
        if (_allMembersLoaded || SelectedDataset is not { IsPartitioned: true }) RefreshVisibleMembers();
        else ScheduleHostFilter();
    }

    partial void OnIsBusyChanged(bool value) => NotifyCommands();

    /// <summary>Off while a review is open: a listing clears the chosen dataset the review belongs to.</summary>
    [RelayCommand(CanExecute = nameof(CanChooseDataset))]
    private Task ListAsync() => RunExclusiveAsync(ListCoreAsync, () => ListAsync());

    private async Task ListCoreAsync(CancellationToken token)
    {
        // The command is off during a review; a Retry left over from an earlier failed listing is not.
        if (IsReviewingUpload) return;
        if (HostPath.DatasetPatternError(Filter) is { } problem)
        {
            StatusText = "✗ " + problem;
            return;
        }
        var pattern = Filter.Trim().ToUpperInvariant();
        StatusText = $"⟳ Listing {pattern}…";
        var listing = await _connection.RunAsync(service => service.ListDatasetsAsync(pattern, new HostListRequest(PageSize), token));
        SelectedDataset = null;
        Datasets.Clear();
        foreach (var entry in listing.Entries) Datasets.Add(new DatasetRow(entry));
        _listedPattern = pattern;
        _datasetContinuation = listing.Continuation;
        HasMoreDatasets = !listing.IsComplete;
        StatusText = DatasetsStatus();
    }

    private Task LoadMembersAsync(DatasetRow? row)
    {
        ClearMembers();
        if (row is not { IsPartitioned: true }) return Task.CompletedTask;
        return RunExclusiveAsync(token => LoadMembersCoreAsync(row, token), () => LoadMembersAsync(SelectedDataset));
    }

    private void ClearMembers()
    {
        Members.Clear();
        VisibleMembers.Clear();
        _memberContinuation = null;
        _memberPattern = null;
        HasMoreMembers = false;
        SetSelectedMembers([]);
        OnPropertyChanged(nameof(MembersHeader));
    }

    /// <summary>The filter narrows the rows here only while the whole library is loaded; otherwise the host has
    /// already applied it and every loaded member is a match.</summary>
    private void RefreshVisibleMembers()
    {
        VisibleMembers.Clear();
        var filter = _allMembersLoaded ? MemberFilter.Trim() : "";
        foreach (var member in Members)
        {
            if (filter.Length == 0 || member.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) VisibleMembers.Add(member);
        }
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        var retry = _retry;
        _retry = null;
        ErrorText = null;
        if (retry is not null) await retry();
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        StatusText = "⟳ Cancelling…";
        _cts?.Cancel();
        Confirmation?.CancelCommand.Execute(null);
    }

    [RelayCommand]
    private async Task OpenGuideAsync()
    {
        if (_openGuide is not null) await _openGuide();
    }

    /// <summary>Runs one operation with the busy flag, its own cancellation, and the failure rules in the class
    /// summary. A second operation while one runs is ignored; the commands are disabled anyway.
    /// <paramref name="describe"/> words the banner; the default is <see cref="HostFileMessages.Describe"/>.</summary>
    private async Task RunExclusiveAsync(Func<CancellationToken, Task> work, Func<Task>? retry = null,
        Func<Exception, string>? describe = null)
    {
        if (IsBusy || _disposed) return;
        using var cts = new CancellationTokenSource();
        _cts = cts;
        IsBusy = true;
        ErrorText = null;
        _retry = null;
        try
        {
            await work(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            StatusText = "– Cancelled.";
        }
        catch (HostFileException ex) when (IsConnectionFailure(ex))
        {
            _retry = retry;
            StatusText = "";
            ErrorText = (describe ?? HostFileMessages.Describe)(ex);
        }
        catch (Exception ex)
        {
            StatusText = "✗ " + HostFileMessages.Describe(ex);
        }
        finally
        {
            _cts = null;
            if (_pinSaveWarning is { } warning)
            {
                _pinSaveWarning = null;
                StatusText = "⚠ " + warning;
            }
            IsBusy = false;
            OnPropertyChanged(nameof(CanRetry));
        }
    }

    /// <summary>A closed browser has no one to ask, so the answer is Cancel.</summary>
    private async Task<ConfirmOutcome> AskAsync(ConfirmationRequest request)
    {
        if (_disposed) return new ConfirmOutcome(ConfirmChoice.Cancel, false);
        Confirmation = request;
        try
        {
            return await request.Answer;
        }
        finally
        {
            Confirmation = null;
        }
    }

    /// <summary>A file dialog that fails to open is a status line, not a failed operation.</summary>
    private async Task<T?> TryPickAsync<T>(Func<Task<T>> pick)
    {
        try
        {
            return await pick();
        }
        catch (Exception ex)
        {
            StatusText = "✗ Could not open the file dialog: " + ex.Message;
            return default;
        }
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    /// <summary>Every command whose CanExecute reads the busy flag, the dataset or the selection. The transfer tasks
    /// add their commands here as they create them.</summary>
    private void NotifyCommands()
    {
        ListCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
        UploadCommand.NotifyCanExecuteChanged();
        StartUploadCommand.NotifyCanExecuteChanged();
        CloseReviewCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        LoadMoreDatasetsCommand.NotifyCanExecuteChanged();
        LoadMoreMembersCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _access.PinSaveFailed -= OnPinSaveFailed;
        _filterDebounce?.Cancel();
        _cts?.Cancel();
        Confirmation?.CancelCommand.Execute(null);
        _connection.Dispose();
    }
}
