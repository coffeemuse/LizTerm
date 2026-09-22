// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.Input;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

public sealed partial class MvsmfBrowserViewModel
{
    private bool CanDownload =>
        !IsBusy && !IsCreating && SelectedDataset is { IsSupported: true } dataset && (dataset.IsSequential || _selectedMembers.Count > 0);

    private string Extension => Mode == HostTransferMode.Text ? ".txt" : "";

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task DownloadAsync() => RunExclusiveAsync(DownloadCoreAsync, () => DownloadAsync());

    private async Task DownloadCoreAsync(CancellationToken token)
    {
        var dataset = SelectedDataset!;
        var options = new DownloadOptions(Mode, TrimTrailingBlanks);
        var extension = Extension;
        if (dataset.IsSequential || _selectedMembers.Count == 1)
        {
            var row = dataset.IsSequential ? null : _selectedMembers[0];
            var path = row?.Path ?? dataset.Path;
            var suggested = (path.Member ?? path.Dataset![(path.Dataset!.LastIndexOf('.') + 1)..]) + extension;
            var file = await TryPickAsync(() => _picker.PickSaveLocationAsync(suggested, $"Download {path}"));
            if (file is null) return;
            if (await BrowserTransfers.DownloadOneAsync(_connection, _access.Etags, _dispatch, path, file, options,
                    text => { if (row is not null) row.Status = text; else StatusText = text; }, token, token))
                StatusText = $"✓ Downloaded {path} to {file}.";
            else if (row is not null) StatusText = row.Status;
            return;
        }

        var members = _selectedMembers.ToList();
        var folder = await TryPickAsync(() => _picker.PickFolderAsync($"Download {members.Count} members of {dataset.Name}"));
        if (folder is null) return;
        foreach (var member in members) member.Status = "";

        var plan = new List<DownloadItem>();
        bool? replaceAll = null;
        foreach (var member in members)
        {
            var file = Path.Combine(folder, member.Name + extension);
            if (File.Exists(file))
            {
                var replace = replaceAll;
                if (replace is null)
                {
                    var answer = await AskAsync(new ConfirmationRequest(
                        $"{Path.GetFileName(file)} already exists in {folder}.", "Replace", "Skip", offersApplyToAll: true));
                    if (answer.Choice == ConfirmChoice.Cancel)
                    {
                        foreach (var row in members) row.Status = "";
                        StatusText = "– Download cancelled.";
                        return;
                    }
                    replace = answer.Choice == ConfirmChoice.Primary;
                    if (answer.ApplyToAll) replaceAll = replace;
                }
                if (replace == false)
                {
                    member.Status = "– Skipped: the file exists";
                    continue;
                }
            }
            member.Status = "⟳ Waiting";
            var target = member;
            plan.Add(new DownloadItem(member.Path, file, text => target.Status = text));
        }

        var done = await BrowserTransfers.DownloadManyAsync(_connection, _access.Etags, _dispatch, plan, options, token);
        if (token.IsCancellationRequested)
        {
            StatusText = "– Download cancelled.";
            return;
        }
        StatusText = $"{(done == members.Count ? "✓" : "⚠")} Downloaded {done} of {Plural(members.Count, "member")} to {folder}.";
    }

    private static bool IsConnectionFailure(HostFileException ex) => BrowserOperations.IsConnectionFailure(ex);
}
