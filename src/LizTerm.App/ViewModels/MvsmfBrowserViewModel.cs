// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.Files;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>mvsMF Access (browser spec §4, pane-pattern spec §4): datasets on the left, members of the chosen PDS
/// on the right, each pane with the verbs that act on its selection, and a window-level status line. One operation
/// at a time; see the partial files for downloads, uploads, delete, manage and create.</summary>
public sealed partial class MvsmfBrowserViewModel : ObservableObject, IDisposable
{
    private readonly HostFileAccess _access;
    private readonly HostFileConnection _connection;
    private readonly IFilePicker _picker;
    private readonly Action<Action> _dispatch;
    private readonly Func<Task>? _openGuide;
    private List<MemberRow> _selectedMembers = [];

    public MvsmfBrowserViewModel(HostFileAccess access, HostFileConnection connection, IFilePicker picker,
        Action<Action> dispatch, Func<Task>? openGuide = null)
    {
        _access = access;
        _connection = connection;
        _picker = picker;
        _dispatch = dispatch;
        _openGuide = openGuide;
        _filter = access.Userid is { Length: > 0 } userid ? userid + ".**" : "";
        Ops = new BrowserOperations();
        Ops.PropertyChanged += OnOperationsChanged;
        _access.PinSaveFailed += OnPinSaveFailed;
        WatchForm();
    }

    /// <summary>The operation that accepted the pin goes on; the warning replaces its status line when it ends.</summary>
    private void OnPinSaveFailed(object? sender, string message) => _dispatch(() =>
    {
        if (!Ops.IsDisposed) Ops.WarnWhenIdle(message);
    });

    public string Title => $"mvsMF Access — {_access.ProfileName} (Preview)";

    public ObservableCollection<DatasetRow> Datasets { get; } = [];
    public ObservableCollection<MemberRow> Members { get; } = [];
    public ObservableCollection<MemberRow> VisibleMembers { get; } = [];

    [ObservableProperty] private string _filter;
    [ObservableProperty] private string _memberFilter = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMembers), nameof(ShowSequentialNote), nameof(ShowChooseHint), nameof(ChooseHint),
        nameof(MembersTitle), nameof(DatasetsFooter), nameof(MembersFooter), nameof(UploadHeader), nameof(ShowMemberPane),
        nameof(ViewHint))]
    private DatasetRow? _selectedDataset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextMode), nameof(IsBinaryMode), nameof(TransferModeLabel))]
    private HostTransferMode _mode = HostTransferMode.Text;

    [ObservableProperty] private bool _trimTrailingBlanks = true;
    [ObservableProperty] private bool _verifyUploads = true;
    [ObservableProperty] private bool _expandTabs = true;

    /// <summary>The runner both tabs share (USS spec §4.4). The window binds to this view model's forwarding
    /// properties below, so its bindings and the older tests see the same names as before the extraction.</summary>
    public BrowserOperations Ops { get; }

    public bool IsBusy => Ops.IsBusy;
    public bool IsIdle => Ops.IsIdle;
    public string StatusText { get => Ops.StatusText; set => Ops.StatusText = value; }
    public string? ErrorText { get => Ops.ErrorText; set => Ops.ErrorText = value; }
    public bool HasError => Ops.HasError;
    public bool CanRetry => Ops.CanRetry;
    public ConfirmationRequest? Confirmation { get => Ops.Confirmation; set => Ops.Confirmation = value; }
    public bool HasConfirmation => Ops.HasConfirmation;
    public IAsyncRelayCommand RetryCommand => Ops.RetryCommand;
    public IRelayCommand CancelCommand => Ops.CancelCommand;

    /// <summary>Re-raises the runner's changes under this view model's names. IsBusy first notifies the commands,
    /// as the generated partial hook did before the extraction, then the property; IsIdle carries
    /// CanChooseDataset with it, as its NotifyPropertyChangedFor did.</summary>
    private void OnOperationsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(BrowserOperations.IsBusy):
                NotifyCommands();
                OnPropertyChanged(nameof(IsBusy));
                break;
            case nameof(BrowserOperations.IsIdle):
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(CanChooseDataset));
                break;
            case { } name:
                OnPropertyChanged(name);
                break;
        }
    }

    private Task RunExclusiveAsync(Func<CancellationToken, Task> work, Func<Task>? retry = null, Func<Exception, string>? describe = null) =>
        Ops.RunExclusiveAsync(work, retry, describe);

    private Task<ConfirmOutcome> AskAsync(ConfirmationRequest request) => Ops.AskAsync(request);

    private void DropRetry() => Ops.DropRetry();

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

    /// <summary>The Members pane's title row: the chosen dataset's name, the review's header while one is open,
    /// and "Members" when nothing supported is chosen (the body then carries the hint).</summary>
    public string MembersTitle => IsReviewingUpload ? UploadHeader
        : SelectedDataset is { IsSupported: true } dataset ? dataset.Name
        : "Members";

    /// <summary>The Datasets pane's footer: the count, a plus while the host has more, and the selection. Empty until
    /// a listing has landed, so a list not yet listed never reads as an empty one. Each operation that refills the
    /// list raises it once at the end, rather than on every row added.</summary>
    public string DatasetsFooter => Datasets.Count == 0 ? (_listedPattern is null ? "" : "No datasets")
        : $"{Counted(Datasets.Count, HasMoreDatasets, "dataset")} · {(SelectedDataset is null ? "none" : "1")} selected";

    /// <summary>The Members pane's footer: the files under review, else the members shown (with a plus while the
    /// host has more, "matching" when the host applied the filter, "n of m" when the filter narrowed the list
    /// here) and how many are selected. Empty for a dataset that has no member list, and until a listing has landed
    /// (still running, failed, cancelled), so an unlisted library never reads as an empty one, DescribeForDelete's
    /// rule. Raised once by whatever refills or narrows the list (RefreshVisibleMembers, SetSelectedMembers).</summary>
    public string MembersFooter
    {
        get
        {
            if (IsReviewingUpload) return Plural(Uploads.Count, "file");
            if (SelectedDataset is not { IsPartitioned: true }) return "";
            if (Members.Count == 0) return _memberPattern is not null ? "No matching members" : _allMembersLoaded ? "No members" : "";
            var shown = _memberPattern is not null ? $"{Members.Count}{(HasMoreMembers ? "+" : "")} matching"
                : VisibleMembers.Count < Members.Count ? $"{VisibleMembers.Count} of {Plural(Members.Count, "member")}"
                : Counted(Members.Count, HasMoreMembers, "member");
            var selected = _selectedMembers.Count == 0 ? "none" : _selectedMembers.Count.ToString(CultureInfo.InvariantCulture);
            return $"{shown} · {selected} selected";
        }
    }

    /// <summary>The status line after a member listing: the count, the pattern the host applied, and whether it
    /// has more.</summary>
    private string MembersStatus() =>
        (_memberPattern is null ? Plural(Members.Count, "member") : $"{Members.Count} matching")
        + (_memberPattern is { } pattern ? " " + pattern : "") + (HasMoreMembers ? " shown, more on the host" : "");

    private static string Counted(int count, bool more, string noun) => more ? $"{count}+ {noun}s" : Plural(count, noun);

    /// <summary>The drop-down button's label (pane-pattern spec §5).</summary>
    public string TransferModeLabel => IsBinaryMode ? "Transfer: Binary" : "Transfer: Text";

    /// <summary>Binary transfers to fixed-length records are padded to whole records (compatibility log,
    /// binary-fixed-padding); the status line says so when Binary is chosen on such a dataset.</summary>
    public const string PaddingNote = "⚠ Binary transfers to fixed-length datasets are padded to whole records.";

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
        OnPropertyChanged(nameof(MembersFooter));
        NotifyCommands();
    }

    partial void OnSelectedDatasetChanged(DatasetRow? value)
    {
        // A pending retry belongs to the dataset it failed on; Retry must never act on another one. So does a
        // filter keystroke still waiting to reach the host: the new dataset's own load applies the filter.
        DropRetry();
        CancelHostFilter();
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

    /// <summary>Off while a review is open: a listing clears the chosen dataset the review belongs to.</summary>
    [RelayCommand(CanExecute = nameof(CanChooseDataset))]
    private Task ListAsync() => RunExclusiveAsync(ListCoreAsync, () => ListAsync());

    /// <summary>False when the listing was refused (a review open, a filter the rules reject, with the reason on the
    /// status line), so an operation that lists on the way to something else knows to stop there.</summary>
    private async Task<bool> ListCoreAsync(CancellationToken token)
    {
        // The command is off during a review; a Retry left over from an earlier failed listing is not.
        if (IsReviewingUpload) return false;
        if (HostPath.DatasetPatternError(Filter) is { } problem)
        {
            StatusText = "✗ " + problem;
            return false;
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
        OnPropertyChanged(nameof(DatasetsFooter));
        StatusText = DatasetsStatus();
        return true;
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
        _allMembersLoaded = false;
        HasMoreMembers = false;
        SetSelectedMembers([]);
        OnPropertyChanged(nameof(MembersFooter));
    }

    /// <summary>The filter narrows the rows here only while the whole library is loaded, read as the host would read
    /// it (<c>*TEXT*</c>, with its wildcards); otherwise the host has already applied it and every loaded member is
    /// a match.</summary>
    private void RefreshVisibleMembers()
    {
        VisibleMembers.Clear();
        var pattern = _allMembersLoaded && MemberFilter.Trim() is { Length: > 0 } text ? $"*{text}*" : null;
        foreach (var member in Members)
        {
            if (pattern is null || HostPath.MemberPatternMatches(pattern, member.Name)) VisibleMembers.Add(member);
        }
        OnPropertyChanged(nameof(MembersFooter));
    }

    [RelayCommand]
    private async Task OpenGuideAsync()
    {
        if (_openGuide is not null) await _openGuide();
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
        RefreshCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
        ViewCommand.NotifyCanExecuteChanged();
        UploadCommand.NotifyCanExecuteChanged();
        StartUploadCommand.NotifyCanExecuteChanged();
        CloseReviewCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        RenameMemberCommand.NotifyCanExecuteChanged();
        RenameDatasetCommand.NotifyCanExecuteChanged();
        DeleteDatasetCommand.NotifyCanExecuteChanged();
        LoadMoreDatasetsCommand.NotifyCanExecuteChanged();
        LoadMoreMembersCommand.NotifyCanExecuteChanged();
        NewDatasetCommand.NotifyCanExecuteChanged();
        CreateCommand.NotifyCanExecuteChanged();
        CloseFormCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (Ops.IsDisposed) return;
        _access.PinSaveFailed -= OnPinSaveFailed;
        _filterDebounce?.Cancel();
        Ops.Dispose();
        _connection.Dispose();
    }
}
