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

    private bool CanDelete => !IsBusy && !IsReviewingUpload && !IsCreating && SelectedDataset is { IsPartitioned: true } && _selectedMembers.Count > 0;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task DeleteAsync() => DeleteMembersAsync(SelectedDataset!, [.. _selectedMembers]);

    /// <summary>A retry deletes the members the connection failure left, not the selection: the failure clears it.
    /// Once every member has gone, only the listing is left to retry.</summary>
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
        if (members.Count == 0)
        {
            await LoadMembersCoreAsync(dataset, token);
            return;
        }
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
                    _access.Etags.Forget(member.Path);
                    deleted++;
                }
                catch (HostFileException ex) when (IsConnectionFailure(ex))
                {
                    // The rest would fail the same way. Show what did go, then let the banner offer Retry, which
                    // asks again about this member and the ones after it. The list is trimmed here rather than asked
                    // of the host, which would only fail again, or open a second sign-in after a cancelled one.
                    stoppedAt([.. members.Skip(i)]);
                    RemoveDeletedMembers(dataset, members.Take(i));
                    throw;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add($"{member.Name}: {HostFileMessages.Describe(ex)}");
                }
            }
            stoppedAt([]);
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

    private void RemoveDeletedMembers(DatasetRow dataset, IEnumerable<MemberRow> deleted)
    {
        if (!ReferenceEquals(SelectedDataset, dataset)) return;
        foreach (var member in deleted) Members.Remove(member);
        RefreshVisibleMembers();
        SetSelectedMembers([]);
        OnPropertyChanged(nameof(MembersFooter));
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
