// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.Input;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>Rename a member or a dataset and delete a dataset (spec §4.4). A rename asks in the strip with a text
/// box; the ETag memory follows every change.</summary>
public sealed partial class MvsmfBrowserViewModel
{
    /// <summary>Raised when an operation wants one member selected (a renamed member under its new name). The window
    /// applies it to its list box, which pushes it back through <see cref="SetSelectedMembers"/>.</summary>
    public event Action<MemberRow>? SelectMemberRequested;

    /// <summary>A dataset the pane's Rename… and Delete… can act on: listed under a name the rules accept. Its
    /// organisation does not matter, since neither operation opens it.</summary>
    private bool CanManageDataset =>
        !IsBusy && !IsReviewingUpload && !IsCreating && SelectedDataset is { } dataset && HostPath.DatasetNameError(dataset.Name) is null;

    private bool CanRenameDataset => CanManageDataset;
    private bool CanDeleteDataset => CanManageDataset;
    private bool CanRenameMember => !IsBusy && !IsReviewingUpload && !IsCreating && SelectedDataset is { IsPartitioned: true } && _selectedMembers.Count == 1;

    // ---- member rename ----

    [RelayCommand(CanExecute = nameof(CanRenameMember))]
    private Task RenameMemberAsync() =>
        SelectedDataset is { } dataset && _selectedMembers is [var member] ? RenameMemberAsync(dataset, member) : Task.CompletedTask;

    /// <summary>A retry before the host has renamed asks again about the same member; once it has, only the listing
    /// is retried (RunThenListAsync).</summary>
    private Task RenameMemberAsync(DatasetRow dataset, MemberRow member) =>
        RunThenListAsync((retryWith, token) => RenameMemberCoreAsync(dataset, member, retryWith, token), () => RenameMemberAsync(dataset, member));

    private async Task RenameMemberCoreAsync(DatasetRow dataset, MemberRow member, Action<Func<Task>> retryWith, CancellationToken token)
    {
        var question = new ConfirmationRequest($"Rename {member.Name} in {dataset.Name} to:", "Rename",
            input: member.Name, inputRule: HostPath.MemberNameError);
        var answer = await AskAsync(question);
        if (answer.Choice != ConfirmChoice.Primary)
        {
            StatusText = "– Rename cancelled.";
            return;
        }
        // The rule passed, so WithMember cannot refuse; it folds the answer (blanks, case) the way the host reads it.
        var to = dataset.Path.WithMember(question.Input);
        StatusText = $"⟳ Renaming {member.Name} to {to.Member}…";
        try
        {
            await _connection.RunAsync(service => service.RenameAsync(member.Path, to.Member!, token));
        }
        catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.NotFound)
        {
            // The member went meanwhile (another user, a job), so the list on screen is stale.
            await LoadMembersCoreAsync(dataset, token);
            StatusText = $"✗ {member.Name}: {HostFileMessages.Describe(ex)}";
            return;
        }
        _access.Etags.Move(member.Path, to);
        retryWith(() => ShowRenamedMemberAsync(dataset, member.Name, to));
        await ShowRenamedMemberAsync(dataset, member.Name, to, token);
    }

    /// <summary>The second half of a member rename, and its own retry: the host has already renamed, so only the
    /// listing is asked again.</summary>
    private Task ShowRenamedMemberAsync(DatasetRow dataset, string oldName, HostPath to) =>
        RunExclusiveAsync(token => ShowRenamedMemberAsync(dataset, oldName, to, token), () => ShowRenamedMemberAsync(dataset, oldName, to));

    private async Task ShowRenamedMemberAsync(DatasetRow dataset, string oldName, HostPath to, CancellationToken token)
    {
        await LoadMembersCoreAsync(dataset, token);
        if (ReferenceEquals(SelectedDataset, dataset) && VisibleMembers.FirstOrDefault(row => row.Name == to.Member) is { } renamed)
        {
            SetSelectedMembers([renamed]);
            SelectMemberRequested?.Invoke(renamed);
        }
        StatusText = $"✓ Renamed {oldName} to {to.Member}.";
    }

    // ---- dataset rename ----

    [RelayCommand(CanExecute = nameof(CanRenameDataset))]
    private Task RenameDatasetAsync() => SelectedDataset is { } dataset ? RenameDatasetAsync(dataset) : Task.CompletedTask;

    private Task RenameDatasetAsync(DatasetRow dataset) =>
        RunThenListAsync((retryWith, token) => RenameDatasetCoreAsync(dataset, retryWith, token), () => RenameDatasetAsync(dataset));

    private async Task RenameDatasetCoreAsync(DatasetRow dataset, Action<Func<Task>> retryWith, CancellationToken token)
    {
        var question = new ConfirmationRequest($"Rename {dataset.Name} to:", "Rename",
            input: dataset.Name, inputRule: HostPath.DatasetNameError);
        var answer = await AskAsync(question);
        if (answer.Choice != ConfirmChoice.Primary)
        {
            StatusText = "– Rename cancelled.";
            return;
        }
        var from = HostPath.ForDataset(dataset.Name);
        var to = HostPath.ForDataset(question.Input);
        StatusText = $"⟳ Renaming {from} to {to}…";
        await _connection.RunAsync(service => service.RenameAsync(from, to.Dataset, token));
        _access.Etags.Move(from, to);
        var what = $"Renamed {from} to {to}";
        retryWith(() => ListAgainAsync(to.Dataset, what, dataset));
        await ShowAfterChangeAsync(to.Dataset, what, dataset, token);
    }

    // ---- dataset delete ----

    [RelayCommand(CanExecute = nameof(CanDeleteDataset))]
    private Task DeleteDatasetAsync() => SelectedDataset is { } dataset ? DeleteDatasetAsync(dataset) : Task.CompletedTask;

    private Task DeleteDatasetAsync(DatasetRow dataset) =>
        RunExclusiveAsync(token => DeleteDatasetCoreAsync(dataset, token), () => DeleteDatasetAsync(dataset));

    private async Task DeleteDatasetCoreAsync(DatasetRow dataset, CancellationToken token)
    {
        var answer = await AskAsync(new ConfirmationRequest($"Delete {DescribeForDelete(dataset)}? This cannot be undone.", $"Delete {dataset.Name}"));
        if (answer.Choice != ConfirmChoice.Primary)
        {
            StatusText = "– Delete cancelled.";
            return;
        }
        var path = HostPath.ForDataset(dataset.Name);
        StatusText = $"⟳ Deleting {path}…";
        await _connection.RunAsync(service => service.DeleteAsync(path, token));
        _access.Etags.ForgetUnder(path);
        if (ReferenceEquals(SelectedDataset, dataset)) SelectedDataset = null;
        Datasets.Remove(dataset);
        StatusText = $"✓ Deleted {dataset.Name}.";
    }

    /// <summary>The name, and what it is: a partitioned dataset with its loaded member count when a member listing
    /// for it landed (a plus while the host has more, or while a host-side member filter is in force, since the
    /// count is then of matches), a partitioned dataset with no count when none landed (cancelled or failed, so
    /// <see cref="Members"/> is empty and would otherwise read as "0 members" — an empty library, not an unlisted
    /// one), a sequential dataset, or the bare name for an organisation the browser cannot open.</summary>
    private string DescribeForDelete(DatasetRow dataset)
    {
        if (dataset.IsPartitioned && ReferenceEquals(SelectedDataset, dataset) && (_allMembersLoaded || HasMoreMembers || _memberPattern is not null))
        {
            var count = HasMoreMembers || _memberPattern is not null ? $"{Members.Count}+ members" : Plural(Members.Count, "member");
            return $"{dataset.Name}, a partitioned dataset with {count}";
        }
        return dataset.IsPartitioned ? $"{dataset.Name}, a partitioned dataset"
            : dataset.IsSequential ? $"{dataset.Name}, a sequential dataset" : dataset.Name;
    }

    // ---- shared ----

    /// <summary>An operation whose second half is a listing (a dataset rename, a create): once the host has done the
    /// first half, a connection failure in the listing must retry only the listing, never ask the host to do the
    /// first half again. The work calls <c>retryWith</c> with the listing's retry at that point.</summary>
    private Task RunThenListAsync(Func<Action<Func<Task>>, CancellationToken, Task> work, Func<Task> retryAll)
    {
        var retry = retryAll;
        return RunExclusiveAsync(token => work(next => retry = next, token), () => retry());
    }

    private Task ListAgainAsync(string name, string what, DatasetRow? gone) =>
        RunExclusiveAsync(token => ShowAfterChangeAsync(name, what, gone, token), () => ListAgainAsync(name, what, gone));

    /// <summary>After a dataset is renamed or created: if the filter in the box is one the rules would refuse (an
    /// emptied box, say), the host has already done its part and the list cannot be asked again until the filter is
    /// fixed, so <paramref name="gone"/> — the old row for a rename, null for a create — is dropped rather than left
    /// stale, and the line says what did and did not happen. Otherwise the filter is listed again, the dataset is
    /// chosen if the listing shows it, and the status line says what happened, adding that the filter hides it when
    /// it does.</summary>
    private async Task ShowAfterChangeAsync(string name, string what, DatasetRow? gone, CancellationToken token)
    {
        if (HostPath.DatasetPatternError(Filter) is { } problem)
        {
            if (gone is not null)
            {
                if (ReferenceEquals(SelectedDataset, gone)) SelectedDataset = null;
                Datasets.Remove(gone);
            }
            StatusText = $"⚠ {what}. The list was not refreshed: {problem}";
            return;
        }
        await ListCoreAsync(token);
        if (Datasets.FirstOrDefault(row => row.Name == name) is { } row)
        {
            await SelectAsync(row, token);
            StatusText = $"✓ {what}.";
        }
        else StatusText = $"✓ {what} (not shown by the filter {_listedPattern}).";
    }

    /// <summary>Chooses <paramref name="row"/> from inside a running operation. Setting SelectedDataset starts a
    /// member load of its own only when nothing runs (RunExclusiveAsync ignores a second operation), so the load is
    /// awaited here.</summary>
    private async Task SelectAsync(DatasetRow row, CancellationToken token)
    {
        SelectedDataset = row;
        if (row.IsPartitioned) await LoadMembersCoreAsync(row, token);
    }
}
