// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>Download…, Upload…, View and the transfer drop-down (USS spec §4.6). A download keeps trailing blanks
/// and an upload keeps tabs: a UNIX file is a byte stream, not records. Each file goes under its own name, case
/// kept; a name already listed asks before it is replaced, a remembered stamp goes out with the write, and a file
/// past the host's cap is refused here, before any request. View reads one file as text, always.</summary>
public sealed partial class UssBrowserViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextMode), nameof(IsBinaryMode), nameof(TransferModeLabel))]
    private HostTransferMode _mode = HostTransferMode.Text;

    [ObservableProperty] private bool _verifyUploads = true;

    /// <summary>The last View's state, null until one has been read; the window opens or reuses its viewer on it.</summary>
    [ObservableProperty] private MvsmfViewerViewModel? _viewer;

    /// <summary>How this platform writes the View gesture; the window sets it once.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewHint))]
    private string _viewGestureText = "";

    private bool _uploadStoppedMidWrite;

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

    public string TransferModeLabel => IsBinaryMode ? "Transfer: Binary" : "Transfer: Text";

    public string ViewHint => ViewGestureText.Length > 0 ? $"View the selected file ({ViewGestureText})" : "View the selected file";

    private bool AllRegularFiles => _selectedFiles.Count > 0 && _selectedFiles.All(file => file.IsFile);
    private bool CanDownload => !IsBusy && AllRegularFiles;
    private bool CanUpload => !IsBusy && Current is not null;
    private bool CanView => !IsBusy && _selectedFiles is [{ IsFile: true }];

    // ---- download ----

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task DownloadAsync() => DownloadFilesAsync([.. _selectedFiles]);

    /// <summary>A retry downloads the files it was asked for, as delete and upload do, never the selection at the
    /// time: a tab round trip or a relist may have changed it.</summary>
    private Task DownloadFilesAsync(IReadOnlyList<FileRow> files) =>
        _ops.RunExclusiveAsync(token => DownloadCoreAsync(files, token), () => DownloadFilesAsync(files));

    /// <summary>Whether <paramref name="target"/> lies inside <paramref name="folder"/>. A separator is added only
    /// when the folder does not already end in one, or a root (<c>/</c>, <c>D:\</c>) or a folder picked with a
    /// trailing separator would read as <c>//</c> and nothing would be under it. (TrimEndingDirectorySeparator
    /// cannot do this: it never trims a root's.)</summary>
    internal static bool IsUnder(string folder, string target)
    {
        var full = System.IO.Path.GetFullPath(folder);
        var prefix = System.IO.Path.EndsInDirectorySeparator(full) ? full : full + System.IO.Path.DirectorySeparatorChar;
        return System.IO.Path.GetFullPath(target).StartsWith(prefix, StringComparison.Ordinal);
    }

    private async Task DownloadCoreAsync(IReadOnlyList<FileRow> files, CancellationToken token)
    {
        var options = new DownloadOptions(Mode, TrimTrailingBlanks: false);
        if (files is [var only])
        {
            var path = only.Path!;
            var file = await _ops.TryPickAsync(() => _picker.PickSaveLocationAsync(only.Name, $"Download {path}"));
            if (file is null) return;
            if (await BrowserTransfers.DownloadOneAsync(_connection, _access.Etags, _dispatch, path, file, options, text => only.Status = text, token, token))
                _ops.StatusText = $"✓ Downloaded {path} to {file}.";
            else _ops.StatusText = only.Status;
            return;
        }

        var folder = await _ops.TryPickAsync(() => _picker.PickFolderAsync($"Download {files.Count} files from {Current}"));
        if (folder is null) return;
        foreach (var row in files) row.Status = "";

        var plan = new List<DownloadItem>();
        bool? replaceAll = null;
        foreach (var row in files)
        {
            var file = System.IO.Path.Combine(folder, row.Name);
            // A UNIX name may hold a character the local file system refuses (a Windows-reserved one) or, combined
            // with the folder, resolve outside it (a rooted name, or one built from '..' segments); the rules
            // HostPath enforces do not rule either out, so both are caught here, before anything is written.
            var hasInvalidChars = row.Name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0;
            var escapesFolder = !IsUnder(folder, file);
            if (hasInvalidChars || escapesFolder)
            {
                row.Status = "– Skipped: the name cannot be a local file name.";
                continue;
            }
            if (File.Exists(file))
            {
                var replace = replaceAll;
                if (replace is null)
                {
                    var answer = await _ops.AskAsync(new ConfirmationRequest($"{row.Name} already exists in {folder}.", "Replace", "Skip", offersApplyToAll: true));
                    if (answer.Choice == ConfirmChoice.Cancel)
                    {
                        foreach (var each in files) each.Status = "";
                        _ops.StatusText = "– Download cancelled.";
                        return;
                    }
                    replace = answer.Choice == ConfirmChoice.Primary;
                    if (answer.ApplyToAll) replaceAll = replace;
                }
                if (replace == false)
                {
                    row.Status = "– Skipped: the file exists";
                    continue;
                }
            }
            row.Status = "⟳ Waiting";
            var target = row;
            plan.Add(new DownloadItem(row.Path!, file, text => target.Status = text));
        }

        var done = await BrowserTransfers.DownloadManyAsync(_connection, _access.Etags, _dispatch, plan, options, token);
        if (token.IsCancellationRequested)
        {
            _ops.StatusText = "– Download cancelled.";
            return;
        }
        _ops.StatusText = $"{(done == files.Count ? "✓" : "⚠")} Downloaded {done} of {Counted(files.Count, "file", "files")} to {folder}.";
    }

    // ---- upload ----

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadAsync()
    {
        if (Current is not { } directory) return;
        var files = await _ops.TryPickAsync(() => _picker.PickFilesToSendAsync($"Upload to {directory}"));
        if (files is not { Count: > 0 }) return;
        await UploadFilesAsync(directory, files);
    }

    /// <summary>A retry sends the files a connection failure left, never the ones already sent.</summary>
    private Task UploadFilesAsync(HostPath directory, IReadOnlyList<string> files)
    {
        var remaining = files;
        return _ops.RunExclusiveAsync(
            token => UploadCoreAsync(directory, files, left => remaining = left, token),
            () => UploadFilesAsync(directory, remaining),
            ex => _uploadStoppedMidWrite ? HostFileMessages.DescribeUnixUploadFailure(ex) : HostFileMessages.Describe(ex));
    }

    /// <summary>One local file on its way up: its name on the host and its check when Text.</summary>
    private sealed record Pending(string LocalPath, string Name, HostPath Path, TextUploadResult? Check);

    private async Task UploadCoreAsync(HostPath directory, IReadOnlyList<string> files, Action<IReadOnlyList<string>> stoppedAt, CancellationToken token)
    {
        _uploadStoppedMidWrite = false;
        var mode = Mode;
        var verify = VerifyUploads;

        // Every file is checked before anything is sent or asked, so a file that cannot go is never asked about.
        var refused = new List<string>();
        var plan = new List<Pending>();
        foreach (var local in files)
        {
            var name = System.IO.Path.GetFileName(local);
            HostPath path;
            try
            {
                path = directory.Child(name);
            }
            catch (ArgumentException ex)
            {
                refused.Add($"{name}: {ex.Message}");
                continue;
            }
            TextUploadResult? check = null;
            try
            {
                if (mode == HostTransferMode.Text)
                {
                    check = HostFileTransfer.CheckUnixTextFile(local, options: new TextUploadOptions(ExpandTabs: false));
                    if (!check.CanUpload)
                    {
                        refused.Add($"{name}: {check.Errors[0].Message}");
                        continue;
                    }
                }
                else if (HostFileTransfer.BinaryUploadProblem(local) is { } problem)
                {
                    refused.Add($"{name}: {problem}");
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                refused.Add($"{name}: Local file: {ex.Message}");
                continue;
            }
            plan.Add(new Pending(local, name, path, check));
        }

        // The rows on screen may be stale, so the directory is listed again before a name is called existing.
        if (!await ListCoreAsync(directory.UnixPath!, keepSelection: true, token)) return;
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        void Show(Pending item, string text)
        {
            results[item.Name] = text;
            if (Files.FirstOrDefault(file => file.Name == item.Name) is { } row) row.Status = text;
        }

        var sending = new List<Pending>();
        bool? replaceAll = null;
        foreach (var item in plan)
        {
            // An entry that is not a regular file (a link, a FIFO, a device) is never written to (USS spec §4.6):
            // refused like a file over the cap, never asked about or sent.
            if (Files.Any(file => file.Name == item.Name && !file.IsFile))
            {
                refused.Add($"{item.Name}: the host has an entry of that name that is not a file");
                continue;
            }
            if (Files.Any(file => file.Name == item.Name))
            {
                var replace = replaceAll;
                if (replace is null)
                {
                    var answer = await _ops.AskAsync(new ConfirmationRequest($"{item.Name} already exists in {directory}.", "Replace", "Skip", offersApplyToAll: true));
                    if (answer.Choice == ConfirmChoice.Cancel)
                    {
                        _ops.StatusText = "– Upload cancelled.";
                        return;
                    }
                    replace = answer.Choice == ConfirmChoice.Primary;
                    if (answer.ApplyToAll) replaceAll = replace;
                }
                if (replace == false)
                {
                    Show(item, "– Skipped: the file exists");
                    continue;
                }
            }
            sending.Add(item);
        }

        var sent = 0;
        var differs = false;
        var cancelled = false;
        for (var i = 0; i < sending.Count; i++)
        {
            var item = sending[i];
            if (token.IsCancellationRequested || cancelled)
            {
                Show(item, "– Cancelled");
                continue;
            }
            Show(item, "⟳ Sending");
            var started = false;
            // Only a file being replaced is checked against its stamp; one this window never downloaded has none.
            var ifMatch = Files.Any(file => file.Name == item.Name) ? _access.Etags.TryGet(item.Path) : null;

            async Task SendAsync(string? stamp)
            {
                started = false;
                if (item.Check is { } check)
                {
                    var outcome = await _connection.RunAsync(service =>
                    {
                        started = true;
                        return HostFileTransfer.UploadTextAsync(service, item.Path, check, verify, stamp, token,
                            written: etag => _access.Etags.Remember(item.Path, etag));
                    });
                    Show(item, outcome.Verification switch
                    {
                        UploadVerification.Matches => "✓ Uploaded and verified",
                        UploadVerification.NotChecked => "✓ Uploaded",
                        _ => $"⚠ Uploaded, but the host copy differs at line {outcome.DiffersAtLine}",
                    });
                    differs |= outcome.Verification == UploadVerification.Differs;
                }
                else
                {
                    var etag = await _connection.RunAsync(service =>
                    {
                        started = true;
                        return HostFileTransfer.UploadBinaryAsync(service, item.Path, item.LocalPath, stamp, token);
                    });
                    _access.Etags.Remember(item.Path, etag);
                    Show(item, "✓ Uploaded");
                }
            }

            try
            {
                try
                {
                    await SendAsync(ifMatch);
                }
                catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.Conflict)
                {
                    // The host checks the stamp before it writes, so nothing was written.
                    var answer = await _ops.AskAsync(new ConfirmationRequest($"{item.Name} changed on the host since you downloaded it.", "Replace anyway", "Skip"));
                    switch (answer.Choice)
                    {
                        case ConfirmChoice.Primary:
                            await SendAsync(null);
                            break;
                        case ConfirmChoice.Secondary:
                            Show(item, "– Skipped: changed on the host");
                            continue;
                        default:
                            cancelled = true;
                            Show(item, "– Cancelled");
                            continue;
                    }
                }
                sent++;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Show(item, started ? "– Cancelled: the file may be partly written" : "– Cancelled");
            }
            catch (HostFileException ex) when (BrowserOperations.IsConnectionFailure(ex))
            {
                foreach (var each in sending.Skip(i + 1)) Show(each, "– Stopped");
                Show(item, "– Stopped: the file may be partly written");
                _uploadStoppedMidWrite = true;
                stoppedAt([.. sending.Skip(i).Select(each => each.LocalPath)]);
                throw;
            }
            catch (Exception ex)
            {
                Show(item, "✗ Failed: " + HostFileMessages.DescribeUnixUploadFailure(ex));
            }
        }
        stoppedAt([]);

        if (token.IsCancellationRequested || cancelled)
        {
            await RefreshQuietlyAsync(directory);
            ApplyResults(results);
            _ops.StatusText = "– Upload cancelled.";
            return;
        }
        // The listing gives the new files their rows; each row then shows what happened to it.
        await ListCoreAsync(directory.UnixPath!, keepSelection: true, token);
        ApplyResults(results);
        var clean = refused.Count == 0 && sent == plan.Count && !differs;
        var summary = $"{(clean ? "✓" : "⚠")} Uploaded {sent} of {Counted(files.Count, "file", "files")} to {directory}.";
        if (refused.Count > 0)
        {
            summary += " ✗ Not sent: " + string.Join("; ", refused.Take(NamesInQuestion));
            if (refused.Count > NamesInQuestion) summary += $" and {refused.Count - NamesInQuestion} more";
        }
        // A file new to the directory whose write failed gets no row from the listing above (the host never had
        // it), so ApplyResults finds nothing to show it on: named here instead, or the reason is lost entirely.
        const string failedMark = "✗ Failed: ";
        var failedWithNoRow = results
            .Where(result => result.Value.StartsWith(failedMark, StringComparison.Ordinal) && Files.All(row => row.Name != result.Key))
            .Select(result => $"{result.Key}: {result.Value[failedMark.Length..]}")
            .ToList();
        if (failedWithNoRow.Count > 0)
        {
            summary += " ✗ Failed: " + string.Join("; ", failedWithNoRow.Take(NamesInQuestion));
            if (failedWithNoRow.Count > NamesInQuestion) summary += $" and {failedWithNoRow.Count - NamesInQuestion} more";
        }
        _ops.StatusText = summary;
    }

    private void ApplyResults(Dictionary<string, string> results)
    {
        foreach (var row in Files)
        {
            if (results.TryGetValue(row.Name, out var text)) row.Status = text;
        }
    }

    // ---- view ----

    [RelayCommand(CanExecute = nameof(CanView))]
    private Task ViewAsync() => _ops.RunExclusiveAsync(ViewCoreAsync, () => ViewAsync());

    private async Task ViewCoreAsync(CancellationToken token)
    {
        if (_selectedFiles is not [{ IsFile: true, Path: { } path }]) return;
        var progress = new BrowserTransfers.RowProgress(_dispatch, bytes => _ops.StatusText = $"⟳ Reading {path} · {BrowserTransfers.Bytes(bytes)} bytes");
        _ops.StatusText = $"⟳ Reading {path}…";
        HostTextRead read;
        try
        {
            // withEtag: false — a view can never write the content back, and a stamp costs the host a second pass.
            read = await _connection.RunAsync(service => service.ReadTextAsync(path, progress, withEtag: false, token));
        }
        finally
        {
            progress.Close();
        }
        // The result is written before the viewer opens: a window that cannot be shown puts its own message here.
        _ops.StatusText = $"✓ Read {path} · {Counted(read.Lines.Count, "line", "lines")}.";
        Viewer = new MvsmfViewerViewModel(path.ToString(), read.Lines, trimTrailingBlanks: false);
    }
}
