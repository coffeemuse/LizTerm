// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

public sealed partial class MvsmfBrowserViewModel
{
    public ObservableCollection<UploadRow> Uploads { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UploadHeader), nameof(ShowMemberPane), nameof(CanChooseDataset))]
    private bool _isReviewingUpload;

    [ObservableProperty] private bool _uploadFinished;
    [ObservableProperty] private string? _reviewMessage;

    /// <summary>Set when a connection failure stopped a write part-way, so the banner warns about a partial member.</summary>
    private bool _uploadStoppedMidWrite;

    /// <summary>The member list gives way to the upload review.</summary>
    public bool ShowMemberPane => ShowMembers && !IsReviewingUpload;

    /// <summary>The dataset list is fixed while an operation runs or a review is open: the review and the member
    /// list both belong to the chosen dataset.</summary>
    public bool CanChooseDataset => !IsBusy && !IsReviewingUpload;

    public string UploadHeader => SelectedDataset is { } dataset ? $"Upload to {dataset.Name}" : "";

    private bool CanUpload => !IsBusy && !IsReviewingUpload && SelectedDataset is { IsSupported: true };
    private bool CanStartUpload => !IsBusy && IsReviewingUpload && !UploadFinished;
    private bool CanCloseReview => !IsBusy && IsReviewingUpload;

    partial void OnIsReviewingUploadChanged(bool value) => NotifyCommands();

    partial void OnUploadFinishedChanged(bool value) => NotifyCommands();

    partial void OnExpandTabsChanged(bool value) => RecheckIfReviewing();

    partial void OnModeChanged(HostTransferMode value) => RecheckIfReviewing();

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadAsync()
    {
        if (SelectedDataset is not { } dataset) return;
        if (dataset.IsSequential)
        {
            await RunExclusiveAsync(token => UploadSequentialAsync(dataset, token), () => UploadAsync(),
                ex => HostFileMessages.DescribeUploadFailure(ex, dataset: true));
            return;
        }
        var files = await TryPickAsync(() => _picker.PickFilesToSendAsync($"Upload to {dataset.Name}"));
        if (files is not { Count: > 0 }) return;
        Uploads.Clear();
        foreach (var file in files) Uploads.Add(new UploadRow(file));
        ReviewMessage = null;
        UploadFinished = false;
        IsReviewingUpload = true;
        Recheck(dataset, Mode, ExpandTabs);
    }

    [RelayCommand(CanExecute = nameof(CanStartUpload))]
    private Task StartUploadAsync() => RunExclusiveAsync(StartUploadCoreAsync, () => StartUploadAsync(),
        ex => _uploadStoppedMidWrite ? HostFileMessages.DescribeUploadFailure(ex) : HostFileMessages.Describe(ex));

    /// <summary>A Retry left by a stopped upload belongs to this review, so it goes with it.</summary>
    [RelayCommand(CanExecute = nameof(CanCloseReview))]
    private void CloseReview()
    {
        _retry = null;
        ErrorText = null;
        OnPropertyChanged(nameof(CanRetry));
        IsReviewingUpload = false;
        UploadFinished = false;
        ReviewMessage = null;
        Uploads.Clear();
    }

    private void RecheckIfReviewing()
    {
        // A running upload checked its files with the settings it started with; the next Start checks them again.
        if (!IsBusy && IsReviewingUpload && !UploadFinished && SelectedDataset is { } dataset) Recheck(dataset, Mode, ExpandTabs);
    }

    private void Recheck(DatasetRow dataset, HostTransferMode mode, bool expandTabs)
    {
        foreach (var row in Uploads)
        {
            row.ReadProblem = null;
            row.Check = null;
            if (mode != HostTransferMode.Text) continue;
            try
            {
                row.Check = HostFileTransfer.CheckTextFile(row.LocalPath, dataset.Attributes, new TextUploadOptions(expandTabs));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                row.ReadProblem = "Local file: " + ex.Message;
            }
        }
    }

    private async Task StartUploadCoreAsync(CancellationToken token)
    {
        if (!IsReviewingUpload) return;
        if (SelectedDataset is not { } dataset)
        {
            ReviewMessage = "✗ Choose the dataset again.";
            return;
        }
        _uploadStoppedMidWrite = false;
        if (UploadFinished)
        {
            // Every row has its result; only the member list after them failed, and this is its retry.
            await LoadMembersCoreAsync(dataset, token);
            return;
        }
        var mode = Mode;
        var expandTabs = ExpandTabs;
        var verify = VerifyUploads;
        Recheck(dataset, mode, expandTabs);
        if (Uploads.Any(row => row.NameProblem is not null))
        {
            ReviewMessage = "✗ Fix the member names marked ✗ first.";
            return;
        }
        if (Uploads.GroupBy(row => row.UploadName).FirstOrDefault(group => group.Count() > 1) is { } clash)
        {
            ReviewMessage = $"✗ Two files would become member {clash.Key}.";
            return;
        }
        ReviewMessage = null;
        var pending = Uploads.Where(row => !row.Sent).ToList();
        foreach (var row in pending)
        {
            row.Status = "";
            row.HostCopyDiffers = false;
        }

        // Asked of the host now, and of the whole library, never of the list on screen: that one can be empty after
        // a failed listing, miss members an earlier, stopped run of this review created, or be one page of many.
        var existing = await ListAllMemberNamesAsync(dataset, token);
        var plan = new List<(UploadRow Row, bool Replaces)>();
        bool? replaceAll = null;
        foreach (var row in pending)
        {
            if (row.IsBlocked)
            {
                row.Status = "– Not sent";
                continue;
            }
            if (existing.Contains(row.UploadName))
            {
                var replace = replaceAll;
                if (replace is null)
                {
                    var answer = await AskAsync(new ConfirmationRequest(
                        $"Member {row.UploadName} already exists in {dataset.Name}.", "Replace", "Skip", offersApplyToAll: true));
                    if (answer.Choice == ConfirmChoice.Cancel)
                    {
                        foreach (var each in pending) each.Status = "";
                        StatusText = "– Upload cancelled.";
                        return;
                    }
                    replace = answer.Choice == ConfirmChoice.Primary;
                    if (answer.ApplyToAll) replaceAll = replace;
                }
                if (replace == false)
                {
                    row.Status = "– Skipped: the member exists";
                    continue;
                }
            }
            row.Status = "⟳ Waiting";
            plan.Add((row, existing.Contains(row.UploadName)));
        }

        var sent = Uploads.Count - pending.Count;
        var cancelled = false;
        foreach (var (row, replaces) in plan)
        {
            if (token.IsCancellationRequested || cancelled)
            {
                row.Status = "– Cancelled";
                continue;
            }
            row.Status = "⟳ Sending";
            var started = false;
            var path = dataset.Path.WithMember(row.UploadName);
            // Only a member being replaced is checked against its stamp (spec §5.2); a member this window never
            // downloaded or wrote has none, and is replaced as before.
            var ifMatch = replaces ? _access.Etags.TryGet(path) : null;

            async Task SendAsync(string? stamp)
            {
                if (mode == HostTransferMode.Text)
                {
                    var check = row.Check!;
                    var outcome = await _connection.RunAsync(service =>
                    {
                        started = true;
                        return HostFileTransfer.UploadTextAsync(service, path, check, verify, stamp, token);
                    });
                    _access.Etags.Remember(path, outcome.Etag);
                    row.Status = Describe(outcome);
                    row.HostCopyDiffers = outcome.Verification == UploadVerification.Differs;
                }
                else
                {
                    var etag = await _connection.RunAsync(service =>
                    {
                        started = true;
                        return HostFileTransfer.UploadBinaryAsync(service, path, row.LocalPath, stamp, token);
                    });
                    _access.Etags.Remember(path, etag);
                    row.Status = "✓ Uploaded";
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
                    // The host checks the stamp before it writes, so nothing was written. Asked per member, with no
                    // Apply to all: a conflict is rare (spec §5.3).
                    var answer = await AskAsync(new ConfirmationRequest(
                        $"{row.UploadName} changed on the host since you downloaded it.", "Replace anyway", "Skip"));
                    switch (answer.Choice)
                    {
                        case ConfirmChoice.Primary:
                            await SendAsync(null);
                            break;
                        case ConfirmChoice.Secondary:
                            row.Status = "– Skipped: changed on the host";
                            continue;
                        default:
                            cancelled = true;
                            row.Status = "– Cancelled";
                            continue;
                    }
                }
                row.Sent = true;
                sent++;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // A write the cancel interrupted may have reached the host in part.
                row.Status = started ? "– Cancelled: the member may be partly written" : "– Cancelled";
            }
            catch (HostFileException ex) when (IsConnectionFailure(ex))
            {
                // Stop here and let the banner offer Retry. The review stays open and unfinished, so Retry (or Start)
                // can run it again; rows already sent are not sent again.
                foreach (var each in plan.SkipWhile(each => !ReferenceEquals(each.Row, row))) each.Row.Status = "– Stopped";
                row.Status = "– Stopped: the member may be partly written";
                _uploadStoppedMidWrite = true;
                throw;
            }
            catch (Exception ex)
            {
                row.Status = "✗ Failed: " + HostFileMessages.DescribeUploadFailure(ex);
            }
        }

        UploadFinished = true;
        if (token.IsCancellationRequested || cancelled)
        {
            StatusText = "– Upload cancelled.";
            return;
        }
        await LoadMembersCoreAsync(dataset, token);
        var clean = sent == Uploads.Count && !Uploads.Any(row => row.HostCopyDiffers);
        StatusText = $"{(clean ? "✓" : "⚠")} Uploaded {sent} of {Plural(Uploads.Count, "file")} to {dataset.Name}.";
    }

    /// <summary>A text file is checked before the question, so a file that cannot be sent is never asked about and
    /// the question names what the check warns of.</summary>
    private async Task UploadSequentialAsync(DatasetRow dataset, CancellationToken token)
    {
        var file = await TryPickAsync(() => _picker.PickFileToSendAsync());
        if (file is null) return;
        if (!ReferenceEquals(SelectedDataset, dataset))
        {
            StatusText = "✗ Choose the dataset again.";
            return;
        }
        var name = Path.GetFileName(file);
        var mode = Mode;
        var verify = VerifyUploads;
        TextUploadResult? check = null;
        if (mode == HostTransferMode.Text)
        {
            try
            {
                check = HostFileTransfer.CheckTextFile(file, dataset.Attributes, new TextUploadOptions(ExpandTabs));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusText = "✗ Not sent: " + HostFileMessages.Describe(ex);
                return;
            }
            if (!check.CanUpload)
            {
                StatusText = "✗ Not sent: " + check.Errors[0].Message;
                return;
            }
        }
        var warnings = string.Concat(check?.Warnings.Select(warning => " ⚠ " + warning.Message) ?? []);
        var answer = await AskAsync(new ConfirmationRequest($"Replace the contents of {dataset.Name} with {name}?{warnings}", "Replace"));
        if (answer.Choice != ConfirmChoice.Primary)
        {
            StatusText = "– Upload cancelled.";
            return;
        }
        async Task SendAsync(string? stamp)
        {
            if (check is not null)
            {
                var outcome = await _connection.RunAsync(service => HostFileTransfer.UploadTextAsync(service, dataset.Path, check, verify, stamp, token));
                _access.Etags.Remember(dataset.Path, outcome.Etag);
                StatusText = outcome.Verification == UploadVerification.Differs
                    ? $"{Describe(outcome)} — {dataset.Name}"
                    : $"✓ Uploaded {name} to {dataset.Name}.";
            }
            else
            {
                var etag = await _connection.RunAsync(service => HostFileTransfer.UploadBinaryAsync(service, dataset.Path, file, stamp, token));
                _access.Etags.Remember(dataset.Path, etag);
                StatusText = $"✓ Uploaded {name} to {dataset.Name}.";
            }
        }

        try
        {
            try
            {
                await SendAsync(_access.Etags.TryGet(dataset.Path));
            }
            catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.Conflict)
            {
                // One file, one dataset: there is nothing to skip to, so the question offers Replace anyway or Cancel.
                var conflict = await AskAsync(new ConfirmationRequest($"{dataset.Name} changed on the host since you downloaded it.", "Replace anyway"));
                if (conflict.Choice != ConfirmChoice.Primary)
                {
                    StatusText = "– Upload cancelled.";
                    return;
                }
                await SendAsync(null);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !(ex is HostFileException host && IsConnectionFailure(host)))
        {
            StatusText = "✗ Failed: " + HostFileMessages.DescribeUploadFailure(ex, dataset: true);
        }
    }

    private static string Describe(UploadOutcome outcome) => outcome.Verification switch
    {
        UploadVerification.Matches => "✓ Uploaded and verified",
        UploadVerification.NotChecked => "✓ Uploaded",
        _ => $"⚠ Uploaded, but the host copy differs at line {outcome.DiffersAtLine}",
    };
}
