// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public class MvsmfBrowserDeleteTests
{
    private static async Task<BrowserTestHost> AskedAsync(BrowserTestHost t, Task running)
    {
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null || running.IsCompleted, "the delete question");
        return t;
    }

    [Fact]
    public async Task Delete_names_the_members_and_removes_them_after_a_yes()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Select("ALLOC", "HELLO");

        var deleting = t.Vm.DeleteCommand.ExecuteAsync(null);
        await AskedAsync(t, deleting);
        var question = t.Vm.Confirmation!;
        Assert.Equal("Delete ALLOC, HELLO from MVSCE02.CNTL? This cannot be undone.", question.Message);
        Assert.Equal("Delete 2 members", question.PrimaryLabel);
        Assert.False(question.HasSecondary);
        question.PrimaryCommand.Execute(null);
        await deleting;

        Assert.Equal(new[] { "COMPILE" }, t.Vm.Members.Select(m => m.Name));
        Assert.Contains("delete:MVSCE02.CNTL(ALLOC)", t.Host.CallsSnapshot());
        Assert.Equal("✓ Deleted 2 of 2 members.", t.Vm.StatusText);
    }

    [Fact]
    public async Task One_member_says_one_member()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Select("HELLO");

        var deleting = t.Vm.DeleteCommand.ExecuteAsync(null);
        await AskedAsync(t, deleting);
        Assert.Equal("Delete 1 member", t.Vm.Confirmation!.PrimaryLabel);
        t.Vm.Confirmation.PrimaryCommand.Execute(null);
        await deleting;
        Assert.Equal("✓ Deleted 1 of 1 member.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Many_members_are_named_up_to_five()
    {
        var t = BrowserTestHost.Create(seed: host => host.AddDataset("A.CNTL", members: ["M1", "M2", "M3", "M4", "M5", "M6", "M7"]));
        t.Vm.Filter = "A.**";
        await t.ChooseAsync("A.CNTL");
        t.Select("M1", "M2", "M3", "M4", "M5", "M6", "M7");

        var deleting = t.Vm.DeleteCommand.ExecuteAsync(null);
        await AskedAsync(t, deleting);

        Assert.Equal("Delete M1, M2, M3, M4, M5 and 2 more from A.CNTL? This cannot be undone.", t.Vm.Confirmation!.Message);
        t.Vm.Confirmation.CancelCommand.Execute(null);
        await deleting;
    }

    [Fact]
    public async Task Cancel_deletes_nothing()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Select("HELLO");

        var deleting = t.Vm.DeleteCommand.ExecuteAsync(null);
        await AskedAsync(t, deleting);
        t.Vm.Confirmation!.CancelCommand.Execute(null);
        await deleting;

        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("delete:"));
        Assert.Equal(3, t.Vm.Members.Count);
    }

    [Fact]
    public async Task A_failed_delete_is_reported_and_the_rest_go()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Select("ALLOC", "HELLO");
        t.Host.Failures["delete:MVSCE02.CNTL(ALLOC)"] = new HostFileException(HostFileErrorKind.NotFound, "x", 5);

        var deleting = t.Vm.DeleteCommand.ExecuteAsync(null);
        await AskedAsync(t, deleting);
        t.Vm.Confirmation!.PrimaryCommand.Execute(null);
        await deleting;

        Assert.Equal("⚠ Deleted 1 of 2 members. ✗ ALLOC: Not found.", t.Vm.StatusText);
        Assert.Equal(new[] { "ALLOC", "COMPILE" }, t.Vm.Members.Select(m => m.Name));
    }

    [Fact]
    public async Task A_connection_failure_stops_the_delete_and_offers_retry()
    {
        var t = await FailedByConnectionAsync();

        Assert.True(t.Vm.HasError);
        Assert.True(t.Vm.CanRetry);
        Assert.DoesNotContain("delete:MVSCE02.CNTL(HELLO)", t.Host.CallsSnapshot());
        Assert.Contains(t.Vm.Members, m => m.Name == "HELLO");
        Assert.Empty(t.Vm.SelectedMembers);

        t.Host.Failures.Clear();
        var retrying = t.Vm.RetryCommand.ExecuteAsync(null);
        await AskedAsync(t, retrying);
        Assert.Equal("Delete ALLOC, HELLO from MVSCE02.CNTL? This cannot be undone.", t.Vm.Confirmation!.Message);
        t.Vm.Confirmation.PrimaryCommand.Execute(null);
        await retrying;

        Assert.False(t.Vm.HasError);
        Assert.Equal(new[] { "COMPILE" }, t.Vm.Members.Select(m => m.Name));
        Assert.Equal("✓ Deleted 2 of 2 members.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Choosing_another_dataset_drops_the_delete_retry()
    {
        var t = await FailedByConnectionAsync();
        Assert.True(t.Vm.CanRetry);

        await t.ChooseAsync("MVSCE02.UFSHOME");

        Assert.False(t.Vm.CanRetry);
    }

    [Fact]
    public async Task Delete_needs_selected_members_of_a_pds()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        Assert.False(t.Vm.DeleteCommand.CanExecute(null));
        t.Select("HELLO");
        Assert.True(t.Vm.DeleteCommand.CanExecute(null));
        await t.ChooseAsync("MVSCE02.UFSHOME");
        Assert.False(t.Vm.DeleteCommand.CanExecute(null));
    }

    private static async Task<BrowserTestHost> FailedByConnectionAsync()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Select("ALLOC", "HELLO");
        t.Host.Failures["delete:MVSCE02.CNTL(ALLOC)"] =
            new HostFileException(HostFileErrorKind.Unreachable, "MVSCE02.CNTL(ALLOC): cannot reach the host (refused).");

        var deleting = t.Vm.DeleteCommand.ExecuteAsync(null);
        await AskedAsync(t, deleting);
        t.Vm.Confirmation!.PrimaryCommand.Execute(null);
        await deleting;
        return t;
    }
}
