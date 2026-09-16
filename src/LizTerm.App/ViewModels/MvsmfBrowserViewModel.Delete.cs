// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.Input;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

public sealed partial class MvsmfBrowserViewModel
{
    private const int NamesInQuestion = 5;

    private bool CanDelete => !IsBusy && !IsReviewingUpload && SelectedDataset is { IsPartitioned: true } && _selectedMembers.Count > 0;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task DeleteAsync() => DeleteMembersAsync(SelectedDataset!, [.. _selectedMembers]);

    /// <summary>A retry deletes the members the connection failure left, not the selection: the refresh after the
    /// failure clears it.</summary>
    private Task DeleteMembersAsync(DatasetRow dataset, IReadOnlyList<MemberRow> members)
    {
        var remaining = members;
        return RunExclusiveAsync(
            token => DeleteCoreAsync(dataset, members, left => remaining = left, token),
            () => DeleteMembersAsync(dataset, remaining));
    }

    private async Task DeleteCoreAsync(DatasetRow dataset, IReadOnlyList<MemberRow> members,
        Action<IReadOnlyList<MemberRow>> stoppedAt, CancellationToken token)
    {
        var names = string.Join(", ", members.Take(NamesInQuestion).Select(member => member.Name));
        if (members.Count > NamesInQuestion) names += $" and {members.Count - NamesInQuestion} more";
        var label = members.Count == 1 ? "Delete 1 member" : $"Delete {members.Count} members";
        var answer = await AskAsync(new ConfirmationRequest($"Delete {names} from {dataset.Name}? This cannot be undone.", label));
        if (answer.Choice != ConfirmChoice.Primary)
        {
            StatusText = "– Delete cancelled.";
            return;
        }

        var deleted = 0;
        var failures = new List<string>();
        try
        {
            for (var i = 0; i < members.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var member = members[i];
                try
                {
                    await _connection.RunAsync(service => service.DeleteAsync(member.Path, token));
                    deleted++;
                }
                catch (HostFileException ex) when (IsConnectionFailure(ex))
                {
                    // The rest would fail the same way. Show what did go, then let the banner offer Retry, which
                    // asks again about this member and the ones after it.
                    stoppedAt([.. members.Skip(i)]);
                    await RefreshAfterDeleteAsync(dataset, token);
                    throw;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add($"{member.Name}: {HostFileMessages.Describe(ex)}");
                }
            }
            await LoadMembersCoreAsync(dataset, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A delete cannot be undone, so the list and the line show what went before the cancel. The list is the
            // truth: a request the cancel interrupted may still have deleted its member on the host.
            await RefreshAfterDeleteAsync(dataset, CancellationToken.None);
            StatusText = $"– Delete cancelled: deleted {deleted} of {Plural(members.Count, "member")}.";
            return;
        }
        StatusText = failures.Count == 0
            ? $"✓ Deleted {deleted} of {Plural(members.Count, "member")}."
            : $"⚠ Deleted {deleted} of {Plural(members.Count, "member")}. ✗ {string.Join("; ", failures)}";
    }

    /// <summary>A refresh on the way out of a stopped delete: its own failure would only hide the reason for the
    /// stop, so it is ignored.</summary>
    private async Task RefreshAfterDeleteAsync(DatasetRow dataset, CancellationToken token)
    {
        try
        {
            await LoadMembersCoreAsync(dataset, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }
    }
}
