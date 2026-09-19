// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>Long lists arrive a page at a time, and a member filter on a partial list is the host's work.</summary>
public class MvsmfBrowserPagingTests
{
    private static BrowserTestHost Paged() => BrowserTestHost.Create(seed: BrowserTestHost.Large, pageSize: 2);

    [Fact]
    public async Task A_long_catalogue_lists_a_page_and_load_more_adds_the_next()
    {
        var t = Paged();

        await t.ListAsync();

        Assert.Equal(new[] { "MVSCE02.CNTL", "MVSCE02.LOAD" }, t.Vm.Datasets.Select(d => d.Name));
        Assert.True(t.Vm.HasMoreDatasets);
        Assert.Equal("2 datasets shown, more on the host", t.Vm.StatusText);
        Assert.Equal(new HostListRequest(MaxItems: 2), t.Host.ListRequests[0]);

        await t.LoadMoreDatasetsAsync();
        await t.LoadMoreDatasetsAsync();
        await t.LoadMoreDatasetsAsync();
        await t.LoadMoreDatasetsAsync();

        Assert.Equal(9, t.Vm.Datasets.Count);
        Assert.False(t.Vm.HasMoreDatasets);
        Assert.False(t.Vm.LoadMoreDatasetsCommand.CanExecute(null));
        Assert.Equal("9 datasets", t.Vm.StatusText);
        Assert.Equal(new HostListRequest(MaxItems: 2, Continuation: "MVSCE02.LOAD"), t.Host.ListRequests[1]);
        Assert.Equal(new HostListRequest(MaxItems: 2, Continuation: "MVSCE02.D3"), t.Host.ListRequests[4]);
    }

    [Fact]
    public async Task Load_more_keeps_the_chosen_dataset_and_listing_again_starts_over()
    {
        var t = Paged();
        await t.ChooseAsync("MVSCE02.CNTL");

        await t.LoadMoreDatasetsAsync();

        Assert.Equal("MVSCE02.CNTL", t.Vm.SelectedDataset?.Name);
        Assert.Equal(2, t.Vm.Members.Count);
        Assert.True(t.Vm.HasMoreMembers);
        Assert.Equal(4, t.Vm.Datasets.Count);

        await t.ListAsync();

        Assert.Equal(2, t.Vm.Datasets.Count);
        Assert.True(t.Vm.HasMoreDatasets);
        Assert.Null(t.Vm.SelectedDataset);
    }

    [Fact]
    public async Task A_small_library_lists_whole_and_offers_no_more()
    {
        var t = Paged();

        await t.ChooseAsync("MVSCE02.LOAD");

        Assert.False(t.Vm.HasMoreMembers);
        Assert.False(t.Vm.LoadMoreMembersCommand.CanExecute(null));
        Assert.Equal("MVSCE02.LOAD · 1 member", t.Vm.MembersHeader);
    }

    [Fact]
    public async Task A_large_library_lists_a_page_of_members_and_load_more_adds_the_next()
    {
        var t = Paged();

        await t.ChooseAsync("MVSCE02.BIG");

        Assert.Equal(new[] { "ALLOC", "COMPILE" }, t.Vm.Members.Select(m => m.Name));
        Assert.True(t.Vm.HasMoreMembers);
        Assert.Equal("MVSCE02.BIG · 2 members shown, more on the host", t.Vm.MembersHeader);
        Assert.Equal(t.Vm.MembersHeader, t.Vm.StatusText);

        await t.LoadMoreMembersAsync();

        Assert.Equal(new[] { "ALLOC", "COMPILE", "HELLO", "LINK" }, t.Vm.VisibleMembers.Select(m => m.Name));
        Assert.True(t.Vm.HasMoreMembers);
        Assert.Equal(new HostListRequest(MaxItems: 2, Continuation: "COMPILE"), t.Host.ListRequests[^1]);

        await t.LoadMoreMembersAsync();

        Assert.Equal(5, t.Vm.Members.Count);
        Assert.False(t.Vm.HasMoreMembers);
        Assert.Equal("MVSCE02.BIG · 5 members", t.Vm.MembersHeader);
    }

    [Fact]
    public async Task A_member_filter_on_a_complete_list_asks_the_host_nothing()
    {
        var t = Paged();
        await t.ChooseAsync("MVSCE02.LOAD");
        Assert.False(t.Vm.HasMoreMembers);
        var requests = t.Host.ListRequests.Count;

        await t.FilterMembersAsync("ro");
        Assert.Equal(new[] { "PROG" }, t.Vm.VisibleMembers.Select(m => m.Name));
        await t.FilterMembersAsync("x");
        Assert.Empty(t.Vm.VisibleMembers);

        Assert.Equal(requests, t.Host.ListRequests.Count);
    }

    [Fact]
    public async Task A_member_filter_on_a_partial_list_is_the_hosts_work()
    {
        var t = Paged();
        await t.ChooseAsync("MVSCE02.BIG");

        await t.FilterMembersAsync("l");

        Assert.Equal(new HostListRequest(MaxItems: 2, NamePattern: "*L*"), t.Host.ListRequests[^1]);
        Assert.Equal(new[] { "ALLOC", "COMPILE" }, t.Vm.VisibleMembers.Select(m => m.Name));
        Assert.True(t.Vm.HasMoreMembers);

        await t.LoadMoreMembersAsync();

        Assert.Equal(new HostListRequest(MaxItems: 2, Continuation: "COMPILE", NamePattern: "*L*"), t.Host.ListRequests[^1]);
        Assert.Equal(new[] { "ALLOC", "COMPILE", "HELLO", "LINK" }, t.Vm.VisibleMembers.Select(m => m.Name));
        Assert.False(t.Vm.HasMoreMembers);
        Assert.Equal("MVSCE02.BIG · 4 members", t.Vm.MembersHeader);

        await t.FilterMembersAsync("");

        Assert.Equal(new HostListRequest(MaxItems: 2), t.Host.ListRequests[^1]);
        Assert.Equal(new[] { "ALLOC", "COMPILE" }, t.Vm.VisibleMembers.Select(m => m.Name));
        Assert.True(t.Vm.HasMoreMembers);
    }

    [Fact]
    public async Task Only_the_last_of_a_burst_of_typing_reaches_the_host()
    {
        var t = Paged();
        await t.ChooseAsync("MVSCE02.BIG");
        // Wide enough that three keystrokes in a row always land inside one pause, whatever the runner is doing.
        t.Vm.FilterDelay = TimeSpan.FromMilliseconds(200);
        var requests = t.Host.ListRequests.Count;

        t.Vm.MemberFilter = "r";
        t.Vm.MemberFilter = "ru";
        await t.FilterMembersAsync("run");

        Assert.Equal(new[] { "RUN" }, t.Vm.VisibleMembers.Select(m => m.Name));
        Assert.Equal(requests + 1, t.Host.ListRequests.Count);
        Assert.Equal("*RUN*", t.Host.ListRequests[^1].NamePattern);
    }

    [Fact]
    public async Task A_member_filter_the_host_cannot_take_is_refused_on_a_partial_list()
    {
        var t = Paged();
        await t.ChooseAsync("MVSCE02.BIG");
        var requests = t.Host.ListRequests.Count;

        await t.FilterMembersAsync("a b");

        Assert.Equal("✗ A member filter cannot contain ' '.", t.Vm.StatusText);
        Assert.Equal(requests, t.Host.ListRequests.Count);
        Assert.Equal(new[] { "ALLOC", "COMPILE" }, t.Vm.VisibleMembers.Select(m => m.Name));
    }

    [Fact]
    public async Task Choosing_a_large_library_with_a_filter_typed_lists_its_matches()
    {
        var t = Paged();
        await t.ChooseAsync("MVSCE02.CNTL");
        await t.FilterMembersAsync("l");

        await t.ChooseAsync("MVSCE02.BIG");
        await Wait.UntilAsync(() => !t.Vm.IsBusy && !t.Vm.IsFilterPending, "the filtered list");

        Assert.Equal("*L*", t.Host.ListRequests[^1].NamePattern);
        Assert.Equal(new[] { "ALLOC", "COMPILE" }, t.Vm.VisibleMembers.Select(m => m.Name));
    }

    [Fact]
    public async Task Choosing_another_dataset_drops_a_filter_still_waiting_to_reach_the_host()
    {
        var t = Paged();
        await t.ChooseAsync("MVSCE02.BIG");
        t.Vm.FilterDelay = TimeSpan.FromMilliseconds(200);

        t.Vm.MemberFilter = "l";
        Assert.True(t.Vm.IsFilterPending);
        await t.ChooseAsync("MVSCE02.CNTL");
        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.False(t.Vm.IsFilterPending);
        // CNTL is partial at two a page, so its own load listed the filtered page once; the stale debounce added nothing.
        Assert.Equal("*L*", t.Host.ListRequests[^1].NamePattern);
        Assert.Equal(1, t.Host.ListRequests.Count(r => r.NamePattern == "*L*"));
    }

    [Fact]
    public async Task A_delete_lists_the_first_page_again()
    {
        var t = Paged();
        await t.ChooseAsync("MVSCE02.BIG");
        await t.LoadMoreMembersAsync();
        t.Select("HELLO");

        var deleting = t.Vm.DeleteCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.HasConfirmation, "the question");
        t.Vm.Confirmation!.PrimaryCommand.Execute(null);
        await deleting;

        Assert.Equal(new[] { "ALLOC", "COMPILE" }, t.Vm.Members.Select(m => m.Name));
        Assert.True(t.Vm.HasMoreMembers);
        Assert.Equal(new HostListRequest(MaxItems: 2), t.Host.ListRequests[^1]);
    }
}
