// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

public sealed partial class MvsmfBrowserViewModel
{
    public const int ParallelDownloads = 2;

    private bool CanDownload =>
        !IsBusy && SelectedDataset is { IsSupported: true } dataset && (dataset.IsSequential || _selectedMembers.Count > 0);

    private string Extension => Mode == HostTransferMode.Text ? ".txt" : "";

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task DownloadAsync() => RunExclusiveAsync(DownloadCoreAsync);

    private async Task DownloadCoreAsync(CancellationToken token)
    {
        var dataset = SelectedDataset!;
        var options = new DownloadOptions(Mode, TrimTrailingBlanks);
        if (dataset.IsSequential || _selectedMembers.Count == 1)
        {
            var row = dataset.IsSequential ? null : _selectedMembers[0];
            var path = row?.Path ?? dataset.Path;
            var suggested = (path.Member ?? path.Dataset[(path.Dataset.LastIndexOf('.') + 1)..]) + Extension;
            var file = await TryPickAsync(() => _picker.PickSaveLocationAsync(suggested, $"Download {path}"));
            if (file is null) return;
            if (await DownloadOneAsync(path, file, options, row, token)) StatusText = $"✓ Downloaded {path} to {file}.";
            else if (row is not null) StatusText = row.Status;
            return;
        }

        var members = _selectedMembers.ToList();
        var folder = await TryPickAsync(() => _picker.PickFolderAsync($"Download {members.Count} members of {dataset.Name}"));
        if (folder is null) return;
        foreach (var member in members) member.Status = "";

        var plan = new List<(MemberRow Row, string File)>();
        bool? replaceAll = null;
        foreach (var member in members)
        {
            var file = Path.Combine(folder, member.Name + Extension);
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
            plan.Add((member, file));
        }

        using var slots = new SemaphoreSlim(ParallelDownloads);
        var results = await Task.WhenAll(plan.Select(async item =>
        {
            try
            {
                await slots.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                item.Row.Status = "– Cancelled";
                return false;
            }
            try
            {
                return await DownloadOneAsync(item.Row.Path, item.File, options, item.Row, token);
            }
            finally
            {
                slots.Release();
            }
        }));

        if (token.IsCancellationRequested)
        {
            StatusText = "– Download cancelled.";
            return;
        }
        var done = results.Count(ok => ok);
        StatusText = $"{(done == members.Count ? "✓" : "⚠")} Downloaded {done} of {Plural(members.Count, "member")} to {folder}.";
    }

    /// <summary>One transfer, reported on the member's row, or on the status line for a sequential dataset.</summary>
    private async Task<bool> DownloadOneAsync(HostPath path, string file, DownloadOptions options, MemberRow? row, CancellationToken token)
    {
        void Show(string text)
        {
            if (row is not null) row.Status = text;
            else StatusText = text;
        }

        var progress = new RowProgress(_dispatch, bytes => Show($"⟳ Running · {Bytes(bytes)} bytes"));
        Show("⟳ Running");
        try
        {
            var written = await _connection.RunAsync(service => HostFileTransfer.DownloadAsync(service, path, file, options, progress, token));
            progress.Close();
            Show($"✓ Done · {Bytes(written)} bytes");
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            progress.Close();
            Show("– Cancelled");
            return false;
        }
        catch (Exception ex)
        {
            progress.Close();
            Show("✗ Failed: " + HostFileMessages.Describe(ex));
            return false;
        }
    }

    private static string Bytes(long count) => count.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Progress arrives on a backend thread and goes through the dispatcher; once the transfer's result is
    /// shown, a report still in the queue must not overwrite it.</summary>
    private sealed class RowProgress(Action<Action> dispatch, Action<long> show) : IProgress<long>
    {
        private volatile bool _closed;

        public void Close() => _closed = true;

        public void Report(long value) => dispatch(() =>
        {
            if (!_closed) show(value);
        });
    }
}
