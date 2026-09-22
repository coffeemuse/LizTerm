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

/// <summary>The USS tab (USS spec §4.6): one directory at a time, its subdirectories in the left pane and its files
/// in the right, anchored on the path row. Runs on the window's shared <see cref="BrowserOperations"/>, so one
/// operation at a time holds across both tabs and every result lands on the one status line. New… and Delete… are
/// in the Manage partial, Download, Upload and View in the Transfers partial.</summary>
public sealed partial class UssBrowserViewModel : ObservableObject
{
    private readonly BrowserOperations _ops;
    private readonly HostFileAccess _access;
    private readonly HostFileConnection _connection;
    private readonly IFilePicker _picker;
    private readonly Action<Action> _dispatch;
    private List<FileRow> _selectedFiles = [];
    private bool _listed;
    private bool _startAttempted;

    public UssBrowserViewModel(BrowserOperations ops, HostFileAccess access, HostFileConnection connection, IFilePicker picker,
        Action<Action> dispatch)
    {
        _ops = ops;
        _access = access;
        _connection = connection;
        _picker = picker;
        _dispatch = dispatch;
        _path = StartPath(access.Userid);
        _ops.PropertyChanged += OnOperationsChanged;
    }

    /// <summary>Where the tab starts: the user's home, <c>/u/&lt;userid&gt;</c> in lower case, or the root when there
    /// is no userid or the name it makes is one the rules refuse (USS spec §3).</summary>
    public static string StartPath(string? userid)
    {
        var name = (userid ?? "").Trim().ToLowerInvariant();
        var home = "/u/" + name;
        return name.Length > 0 && HostPath.UnixPathError(home) is null ? home : "/";
    }

    public ObservableCollection<DirectoryRow> Directories { get; } = [];
    public ObservableCollection<FileRow> Files { get; } = [];

    /// <summary>The path row's text. Go lists it; a listing that lands writes the listed path back, trimmed.</summary>
    [ObservableProperty] private string _path;

    /// <summary>The directory the panes show; null until a listing has landed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrent), nameof(FilesTitle))]
    private HostPath? _current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectoriesFooter))]
    private DirectoryRow? _selectedDirectory;

    public bool IsBusy => _ops.IsBusy;
    public bool HasCurrent => Current is not null;

    /// <summary>The Files pane's title: the current path, or "Files" before the first listing.</summary>
    public string FilesTitle => Current?.UnixPath ?? "Files";

    /// <summary>Empty until a listing has landed, so an unlisted directory never reads as an empty one.</summary>
    public string DirectoriesFooter => !_listed ? "" : Directories.Count == 0 ? "No directories"
        : $"{BrowserTransfers.Counted(Directories.Count, "directory", "directories")} · {(SelectedDirectory is null ? "none" : "1")} selected";

    public string FilesFooter => !_listed ? "" : Files.Count == 0 ? "No files"
        : $"{BrowserTransfers.Counted(Files.Count, "file", "files")} · {(_selectedFiles.Count == 0 ? "none" : _selectedFiles.Count.ToString(CultureInfo.InvariantCulture))} selected";

    public IReadOnlyList<FileRow> SelectedFiles => _selectedFiles;

    /// <summary>Raised when an operation wants these file rows selected (a refresh keeping the selection). The
    /// window applies it to its list box, which pushes it back through <see cref="SetSelectedFiles"/>.</summary>
    public event Action<IReadOnlyList<FileRow>>? SelectFilesRequested;

    /// <summary>The window pushes the file list's selection here, as the Members pane does.</summary>
    public void SetSelectedFiles(IEnumerable<FileRow> files)
    {
        _selectedFiles = [.. files];
        OnPropertyChanged(nameof(SelectedFiles));
        OnPropertyChanged(nameof(FilesFooter));
        NotifyCommands();
    }

    partial void OnSelectedDirectoryChanged(DirectoryRow? value) => NotifyCommands();

    private void OnOperationsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BrowserOperations.IsBusy)) return;
        OnPropertyChanged(nameof(IsBusy));
        NotifyCommands();
    }

    private bool CanGo => !IsBusy;
    private bool CanUp => !IsBusy && Current?.Parent is not null;
    private bool CanOpenDirectory => !IsBusy && SelectedDirectory is { Path: not null };
    private bool CanRefresh => !IsBusy && Current is not null;

    [RelayCommand(CanExecute = nameof(CanGo))]
    private Task GoAsync() => ListPathAsync(Path);

    [RelayCommand(CanExecute = nameof(CanUp))]
    private Task UpAsync() => Current?.Parent is { } parent ? ListPathAsync(parent.UnixPath!) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanOpenDirectory))]
    private Task OpenDirectoryAsync() => SelectedDirectory is { Path: { } path } ? ListPathAsync(path.UnixPath!) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => Current is { } current ? ListPathAsync(current.UnixPath!, keepSelection: true) : Task.CompletedTask;

    /// <summary>The first time the tab is shown: lists the start path, once, so a dataset-only user pays nothing.
    /// Once means attempted, not succeeded: a start path the host does not have (USS spec §3), a refusal, a
    /// connection failure or a Cancel leaves its reason on the status line and never starts the next attempt.</summary>
    public Task EnsureListedAsync()
    {
        if (_startAttempted || IsBusy) return Task.CompletedTask;
        _startAttempted = true;
        return ListPathAsync(Path);
    }

    private Task ListPathAsync(string text, bool keepSelection = false) =>
        _ops.RunExclusiveAsync(token => ListCoreAsync(text, keepSelection, token), () => ListPathAsync(text, keepSelection));

    /// <summary>False when the listing was refused (a path the rules reject, one the host does not have, one that
    /// is a file), with the reason on the status line, the box keeping its text and the panes their last listing,
    /// so an operation that lists on the way to something else knows to stop there.</summary>
    internal async Task<bool> ListCoreAsync(string text, bool keepSelection, CancellationToken token)
    {
        var trimmed = (text ?? "").Trim();
        if (HostPath.UnixPathError(trimmed) is { } problem)
        {
            _ops.StatusText = "✗ " + problem;
            return false;
        }
        var target = HostPath.ForUnix(trimmed);
        _ops.StatusText = $"⟳ Listing {target}…";
        HostFileListing listing;
        try
        {
            listing = await _connection.RunAsync(service => service.ListDirectoryAsync(target, HostListRequest.All, token));
        }
        catch (HostFileException ex) when (ex.Kind is HostFileErrorKind.NotFound or HostFileErrorKind.InvalidRequest)
        {
            _ops.StatusText = ex.Kind == HostFileErrorKind.NotFound
                ? $"✗ File not found: {target}"
                : $"✗ {target}: {HostFileMessages.Describe(ex)}";
            return false;
        }
        Fill(target, listing, keepSelection);
        return true;
    }

    /// <summary>Fills the panes from a listing, sorted by name (ordinal, as the host compares): the host lists in
    /// its own order. A selection kept by name survives a refresh; the file rows go back through the window.</summary>
    private void Fill(HostPath target, HostFileListing listing, bool keepSelection)
    {
        var keptDirectory = keepSelection ? SelectedDirectory?.Name : null;
        var keptFiles = keepSelection ? _selectedFiles.Select(f => f.Name).ToHashSet(StringComparer.Ordinal) : [];
        Current = target;
        Path = target.UnixPath!;
        Directories.Clear();
        Files.Clear();
        foreach (var entry in listing.Entries.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            if (entry.Kind == HostFileEntryKind.Directory) Directories.Add(new DirectoryRow(target, entry));
            else Files.Add(new FileRow(target, entry));
        }
        _listed = true;
        SelectedDirectory = Directories.FirstOrDefault(d => d.Name == keptDirectory);
        var kept = Files.Where(f => keptFiles.Contains(f.Name)).ToList();
        SetSelectedFiles(kept);
        if (kept.Count > 0) SelectFilesRequested?.Invoke(kept);
        OnPropertyChanged(nameof(DirectoriesFooter));
        _ops.StatusText = $"✓ Listed {target} · {BrowserTransfers.Counted(Directories.Count, "directory", "directories")}, {BrowserTransfers.Counted(Files.Count, "file", "files")}"
            // The host cut the listing short and offers no way to continue it (USS spec §3).
            + (listing.Truncated ? " · more on the host" : "") + ".";
    }

    /// <summary>The entry's path under its directory, or null for a name the rules refuse: a host can list anything.</summary>
    internal static HostPath? ChildOrNull(HostPath parent, string name)
    {
        try
        {
            return parent.Child(name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The MODIFIED column: local time to the minute, or empty when the host gave none.</summary>
    internal static string FormatModified(DateTimeOffset? when) =>
        when?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "";

    /// <summary>Every command whose CanExecute reads the busy flag or a selection. The other partials add theirs.</summary>
    private void NotifyCommands()
    {
        GoCommand.NotifyCanExecuteChanged();
        UpCommand.NotifyCanExecuteChanged();
        OpenDirectoryCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        NewDirectoryCommand.NotifyCanExecuteChanged();
        DeleteDirectoryCommand.NotifyCanExecuteChanged();
        DeleteFilesCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
        UploadCommand.NotifyCanExecuteChanged();
        ViewCommand.NotifyCanExecuteChanged();
    }
}
