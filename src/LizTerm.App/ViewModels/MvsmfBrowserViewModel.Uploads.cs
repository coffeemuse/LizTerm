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
        var dataset = SelectedDataset!;
        if (dataset.IsSequential)
        {
            await RunExclusiveAsync(token => UploadSequentialAsync(dataset, token), () => UploadAsync(),
                HostFileMessages.DescribeUploadFailure);
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

    [RelayCommand(CanExecute = nameof(CanCloseReview))]
    private void CloseReview()
    {
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
        var dataset = SelectedDataset!;
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
        foreach (var row in pending) row.Status = "";

        // Asked of the host now, never of the list on screen: that one can be empty after a failed listing, or miss
        // members an earlier, stopped run of this review created.
        var listed = await LoadMembersCoreAsync(dataset, token);
        var existing = listed.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        var plan = new List<UploadRow>();
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
            plan.Add(row);
        }

        var sent = Uploads.Count - pending.Count;
        foreach (var row in plan)
        {
            if (token.IsCancellationRequested)
            {
                row.Status = "– Cancelled";
                continue;
            }
            row.Status = "⟳ Sending";
            var path = dataset.Path.WithMember(row.UploadName);
            try
            {
                if (mode == HostTransferMode.Text)
                {
                    var check = row.Check!;
                    var outcome = await _connection.RunAsync(service => HostFileTransfer.UploadTextAsync(service, path, check, verify, token));
                    row.Status = Describe(outcome);
                }
                else
                {
                    await _connection.RunAsync(service => HostFileTransfer.UploadBinaryAsync(service, path, row.LocalPath, token));
                    row.Status = "✓ Uploaded";
                }
                row.Sent = true;
                sent++;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                row.Status = "– Cancelled";
            }
            catch (HostFileException ex) when (IsConnectionFailure(ex))
            {
                // Stop here and let the banner offer Retry. The review stays open and unfinished, so Retry (or Start)
                // can run it again; rows already sent are not sent again.
                foreach (var each in plan.SkipWhile(each => !ReferenceEquals(each, row))) each.Status = "– Stopped";
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
        if (token.IsCancellationRequested)
        {
            StatusText = "– Upload cancelled.";
            return;
        }
        await LoadMembersCoreAsync(dataset, token);
        StatusText = $"{(sent == Uploads.Count ? "✓" : "⚠")} Uploaded {sent} of {Plural(Uploads.Count, "file")} to {dataset.Name}.";
    }

    private async Task UploadSequentialAsync(DatasetRow dataset, CancellationToken token)
    {
        var file = await TryPickAsync(() => _picker.PickFileToSendAsync());
        if (file is null) return;
        var name = Path.GetFileName(file);
        var answer = await AskAsync(new ConfirmationRequest($"Replace the contents of {dataset.Name} with {name}?", "Replace"));
        if (answer.Choice != ConfirmChoice.Primary)
        {
            StatusText = "– Upload cancelled.";
            return;
        }
        try
        {
            if (Mode == HostTransferMode.Text)
            {
                var check = HostFileTransfer.CheckTextFile(file, dataset.Attributes, new TextUploadOptions(ExpandTabs));
                if (!check.CanUpload)
                {
                    StatusText = "✗ Not sent: " + check.Errors[0].Message;
                    return;
                }
                var outcome = await _connection.RunAsync(service => HostFileTransfer.UploadTextAsync(service, dataset.Path, check, VerifyUploads, token));
                StatusText = outcome.Verification == UploadVerification.Differs
                    ? $"{Describe(outcome)} — {dataset.Name}"
                    : $"✓ Uploaded {name} to {dataset.Name}.";
            }
            else
            {
                await _connection.RunAsync(service => HostFileTransfer.UploadBinaryAsync(service, dataset.Path, file, token));
                StatusText = $"✓ Uploaded {name} to {dataset.Name}.";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !(ex is HostFileException host && IsConnectionFailure(host)))
        {
            StatusText = "✗ Failed: " + HostFileMessages.DescribeUploadFailure(ex);
        }
    }

    private static string Describe(UploadOutcome outcome) => outcome.Verification switch
    {
        UploadVerification.Matches => "✓ Uploaded and verified",
        UploadVerification.NotChecked => "✓ Uploaded",
        _ => $"⚠ Uploaded, but the host copy differs at line {outcome.DiffersAtLine}",
    };
}
