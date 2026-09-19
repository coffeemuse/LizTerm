// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public class MvsmfBrowserManageTests
{
    private static async Task<ConfirmationRequest> AskedAsync(BrowserTestHost t, Task running)
    {
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null || running.IsCompleted, "the question");
        Assert.False(running.IsCompleted, "the operation ended without asking: " + t.Vm.StatusText + " " + t.Vm.ErrorText);
        return t.Vm.Confirmation!;
    }

    private static void Answer(ConfirmationRequest question, string input)
    {
        question.Input = input;
        Assert.True(question.CanAnswerPrimary, question.InputProblem);
        question.PrimaryCommand.Execute(null);
    }

    // ---- member rename ----

    [Fact]
    public async Task Rename_member_asks_with_the_old_name_and_renames_on_the_host()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Select("HELLO");
        t.Access.Etags.Remember(HostPath.ForMember("MVSCE02.CNTL", "HELLO"), "a");
        var selected = new List<IReadOnlyList<MemberRow>>();
        t.Vm.SelectMemberRequested += row => selected.Add([row]);

        var renaming = t.Vm.RenameMemberCommand.ExecuteAsync(null);
        var question = await AskedAsync(t, renaming);
        Assert.Equal("Rename HELLO in MVSCE02.CNTL to:", question.Message);
        Assert.Equal("Rename", question.PrimaryLabel);
        Assert.False(question.HasSecondary);
        Assert.Equal("HELLO", question.Input);
        Answer(question, "hello2");
        await renaming;

        Assert.Contains("rename:MVSCE02.CNTL(HELLO):HELLO2", t.Host.CallsSnapshot());
        Assert.Equal(new[] { "ALLOC", "COMPILE", "HELLO2" }, t.Vm.Members.Select(m => m.Name));
        Assert.Equal(new[] { "HELLO2" }, t.Vm.SelectedMembers.Select(m => m.Name));
        Assert.Equal(new[] { "HELLO2" }, selected.Single().Select(m => m.Name));
        Assert.Equal("✓ Renamed HELLO to HELLO2.", t.Vm.StatusText);
        Assert.Null(t.Access.Etags.TryGet(HostPath.ForMember("MVSCE02.CNTL", "HELLO")));
        Assert.Equal("a", t.Access.Etags.TryGet(HostPath.ForMember("MVSCE02.CNTL", "HELLO2")));
    }

    [Fact]
    public async Task Rename_member_cancelled_renames_nothing()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Select("HELLO");

        var renaming = t.Vm.RenameMemberCommand.ExecuteAsync(null);
        (await AskedAsync(t, renaming)).CancelCommand.Execute(null);
        await renaming;

        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("rename:"));
        Assert.Equal("– Rename cancelled.", t.Vm.StatusText);
    }

    [Fact]
    public async Task A_member_that_went_meanwhile_reloads_the_list()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Select("HELLO");
        t.Host.Members["MVSCE02.CNTL"].Remove("HELLO");

        var renaming = t.Vm.RenameMemberCommand.ExecuteAsync(null);
        Answer(await AskedAsync(t, renaming), "HELLO2");
        await renaming;

        Assert.Equal(new[] { "ALLOC", "COMPILE" }, t.Vm.Members.Select(m => m.Name));
        Assert.Equal("✗ HELLO: Not found.", t.Vm.StatusText);
    }

    [Fact]
    public async Task A_member_renamed_onto_an_existing_name_is_the_hosts_sentence()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Select("HELLO");

        var renaming = t.Vm.RenameMemberCommand.ExecuteAsync(null);
        Answer(await AskedAsync(t, renaming), "ALLOC");
        await renaming;

        Assert.Equal("✗ Rename MVSCE02.CNTL(HELLO) to ALLOC: a member of that name already exists.", t.Vm.StatusText);
        Assert.Equal(new[] { "ALLOC", "COMPILE", "HELLO" }, t.Vm.Members.Select(m => m.Name));
    }

    [Fact]
    public async Task A_connection_failure_on_a_member_rename_offers_retry_which_asks_again()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Select("HELLO");
        t.Host.Failures["rename:MVSCE02.CNTL(HELLO):HELLO2"] =
            new HostFileException(HostFileErrorKind.Unreachable, "MVSCE02.CNTL(HELLO): cannot reach the host (refused).");

        var renaming = t.Vm.RenameMemberCommand.ExecuteAsync(null);
        Answer(await AskedAsync(t, renaming), "HELLO2");
        await renaming;
        Assert.True(t.Vm.CanRetry);

        t.Host.Failures.Clear();
        var retrying = t.Vm.RetryCommand.ExecuteAsync(null);
        var question = await AskedAsync(t, retrying);
        Assert.Equal("Rename HELLO in MVSCE02.CNTL to:", question.Message);
        Answer(question, "HELLO2");
        await retrying;

        Assert.False(t.Vm.HasError);
        Assert.Equal("✓ Renamed HELLO to HELLO2.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Rename_member_needs_exactly_one_member_of_a_pds()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        Assert.False(t.Vm.RenameMemberCommand.CanExecute(null));
        t.Select("HELLO");
        Assert.True(t.Vm.RenameMemberCommand.CanExecute(null));
        t.Select("HELLO", "ALLOC");
        Assert.False(t.Vm.RenameMemberCommand.CanExecute(null));
        await t.ChooseAsync("MVSCE02.UFSHOME");
        Assert.False(t.Vm.RenameMemberCommand.CanExecute(null));
    }

    // ---- dataset rename ----

    [Fact]
    public async Task Rename_dataset_relists_and_chooses_the_new_name()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Access.Etags.Remember(HostPath.ForMember("MVSCE02.CNTL", "HELLO"), "a");

        var renaming = t.Vm.RenameDatasetCommand.ExecuteAsync(null);
        var question = await AskedAsync(t, renaming);
        Assert.Equal("Rename MVSCE02.CNTL to:", question.Message);
        Assert.Equal("MVSCE02.CNTL", question.Input);
        Answer(question, "mvsce02.jcl");
        await renaming;

        Assert.Contains("rename:MVSCE02.CNTL:MVSCE02.JCL", t.Host.CallsSnapshot());
        Assert.Equal("MVSCE02.JCL", t.Vm.SelectedDataset?.Name);
        Assert.DoesNotContain(t.Vm.Datasets, d => d.Name == "MVSCE02.CNTL");
        Assert.Equal(new[] { "ALLOC", "COMPILE", "HELLO" }, t.Vm.Members.Select(m => m.Name));
        Assert.Equal("✓ Renamed MVSCE02.CNTL to MVSCE02.JCL.", t.Vm.StatusText);
        Assert.Equal("a", t.Access.Etags.TryGet(HostPath.ForMember("MVSCE02.JCL", "HELLO")));
        Assert.Null(t.Access.Etags.TryGet(HostPath.ForMember("MVSCE02.CNTL", "HELLO")));
    }

    [Fact]
    public async Task Rename_dataset_outside_the_filter_says_so()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.UFSHOME");

        var renaming = t.Vm.RenameDatasetCommand.ExecuteAsync(null);
        Answer(await AskedAsync(t, renaming), "OTHER.UFSHOME");
        await renaming;

        Assert.Null(t.Vm.SelectedDataset);
        Assert.Equal("✓ Renamed MVSCE02.UFSHOME to OTHER.UFSHOME (not shown by the filter MVSCE02.**).", t.Vm.StatusText);
    }

    [Fact]
    public async Task Rename_dataset_onto_an_existing_name_is_the_hosts_server_error()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");

        var renaming = t.Vm.RenameDatasetCommand.ExecuteAsync(null);
        Answer(await AskedAsync(t, renaming), "MVSCE02.LOAD");
        await renaming;

        Assert.Equal("✗ Server error: Rename operation failed (reason 8).", t.Vm.StatusText);
        Assert.Equal("MVSCE02.CNTL", t.Vm.SelectedDataset?.Name);
    }

    [Fact]
    public async Task A_listing_that_fails_after_a_rename_retries_only_the_listing()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Host.Failures["list:MVSCE02.**"] = new HostFileException(HostFileErrorKind.Unreachable, "cannot reach the host (refused).");

        var renaming = t.Vm.RenameDatasetCommand.ExecuteAsync(null);
        Answer(await AskedAsync(t, renaming), "MVSCE02.JCL");
        await renaming;
        Assert.True(t.Vm.CanRetry);

        t.Host.Failures.Clear();
        await t.Vm.RetryCommand.ExecuteAsync(null);

        Assert.False(t.Vm.HasError);
        Assert.Equal(1, t.Host.CallsSnapshot().Count(c => c.StartsWith("rename:")));
        Assert.Equal("MVSCE02.JCL", t.Vm.SelectedDataset?.Name);
        Assert.Equal("✓ Renamed MVSCE02.CNTL to MVSCE02.JCL.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Rename_and_delete_dataset_need_a_selected_dataset_with_an_acceptable_name()
    {
        var t = BrowserTestHost.Create(seed: host =>
        {
            BrowserTestHost.Standard(host);
            host.Datasets.Add(new HostFileEntry("MVSCE02.BAD..NAME", HostFileEntryKind.Dataset, new DatasetAttributes("PO", "FB", 80, 3120, "PUB000")));
        });
        await t.ListAsync();
        Assert.False(t.Vm.RenameDatasetCommand.CanExecute(null));
        Assert.False(t.Vm.DeleteDatasetCommand.CanExecute(null));

        await t.ChooseAsync("MVSCE02.DB");
        Assert.True(t.Vm.RenameDatasetCommand.CanExecute(null));
        Assert.True(t.Vm.DeleteDatasetCommand.CanExecute(null));

        t.Vm.SelectedDataset = t.Vm.Datasets.Single(d => d.Name == "MVSCE02.BAD..NAME");
        Assert.False(t.Vm.RenameDatasetCommand.CanExecute(null));
        Assert.False(t.Vm.DeleteDatasetCommand.CanExecute(null));
    }

    // ---- dataset delete ----

    [Fact]
    public async Task Delete_dataset_names_the_pds_with_its_member_count()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Access.Etags.Remember(HostPath.ForMember("MVSCE02.CNTL", "HELLO"), "a");
        t.Access.Etags.Remember(HostPath.ForMember("MVSCE02.LOAD", "PROG"), "b");

        var deleting = t.Vm.DeleteDatasetCommand.ExecuteAsync(null);
        var question = await AskedAsync(t, deleting);
        Assert.Equal("Delete MVSCE02.CNTL, a partitioned dataset with 3 members? This cannot be undone.", question.Message);
        Assert.Equal("Delete MVSCE02.CNTL", question.PrimaryLabel);
        Assert.False(question.HasInput);
        question.PrimaryCommand.Execute(null);
        await deleting;

        Assert.Contains("delete:MVSCE02.CNTL", t.Host.CallsSnapshot());
        Assert.DoesNotContain(t.Vm.Datasets, d => d.Name == "MVSCE02.CNTL");
        Assert.Null(t.Vm.SelectedDataset);
        Assert.Empty(t.Vm.Members);
        Assert.Equal("✓ Deleted MVSCE02.CNTL.", t.Vm.StatusText);
        Assert.Null(t.Access.Etags.TryGet(HostPath.ForMember("MVSCE02.CNTL", "HELLO")));
        Assert.Equal("b", t.Access.Etags.TryGet(HostPath.ForMember("MVSCE02.LOAD", "PROG")));
    }

    [Fact]
    public async Task Delete_dataset_says_plus_while_the_host_has_more_members()
    {
        var t = BrowserTestHost.Create(seed: BrowserTestHost.Large, pageSize: 2);
        await t.ChooseAsync("MVSCE02.BIG");
        Assert.True(t.Vm.HasMoreMembers);

        var deleting = t.Vm.DeleteDatasetCommand.ExecuteAsync(null);
        var question = await AskedAsync(t, deleting);
        Assert.Equal("Delete MVSCE02.BIG, a partitioned dataset with 2+ members? This cannot be undone.", question.Message);
        question.CancelCommand.Execute(null);
        await deleting;
    }

    [Fact]
    public async Task Delete_dataset_words_a_sequential_and_an_unsupported_dataset()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.UFSHOME");
        var deleting = t.Vm.DeleteDatasetCommand.ExecuteAsync(null);
        var question = await AskedAsync(t, deleting);
        Assert.Equal("Delete MVSCE02.UFSHOME, a sequential dataset? This cannot be undone.", question.Message);
        question.CancelCommand.Execute(null);
        await deleting;
        Assert.Equal("– Delete cancelled.", t.Vm.StatusText);

        await t.ChooseAsync("MVSCE02.DB");
        deleting = t.Vm.DeleteDatasetCommand.ExecuteAsync(null);
        question = await AskedAsync(t, deleting);
        Assert.Equal("Delete MVSCE02.DB? This cannot be undone.", question.Message);
        question.PrimaryCommand.Execute(null);
        await deleting;
        Assert.Equal("✓ Deleted MVSCE02.DB.", t.Vm.StatusText);
        Assert.Equal(3, t.Vm.Datasets.Count);
    }

    [Fact]
    public async Task A_dataset_delete_that_fails_keeps_the_row()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Host.Failures["delete:MVSCE02.CNTL"] = new HostFileException(HostFileErrorKind.NotAuthorized, "x", 6);

        var deleting = t.Vm.DeleteDatasetCommand.ExecuteAsync(null);
        (await AskedAsync(t, deleting)).PrimaryCommand.Execute(null);
        await deleting;

        Assert.Equal("✗ Not authorized.", t.Vm.StatusText);
        Assert.Equal("MVSCE02.CNTL", t.Vm.SelectedDataset?.Name);
        Assert.Equal(4, t.Vm.Datasets.Count);
    }
}
