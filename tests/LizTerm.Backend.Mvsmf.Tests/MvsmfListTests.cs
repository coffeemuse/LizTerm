// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfListTests
{
    private static MvsmfFileService Service(RecordedHandler handler) =>
        new(handler, MvsmfAuthTests.Base, MvsmfAuthTests.Providing([], new HostCredentials("MVSCE02", "pw")));

    [Fact]
    public async Task Dataset_list_reads_names_and_attributes()
    {
        var handler = new RecordedHandler().Then("login-200").Then("ds-list-sys1");
        using var service = Service(handler);

        var listing = await service.ListDatasetsAsync("sys1.**", HostListRequest.All, TestContext.Current.CancellationToken);
        var entries = listing.Entries;

        Assert.True(entries.Count > 3);
        Assert.True(listing.IsComplete);
        Assert.Equal("SYS1.ACMDLIB", entries[0].Name);
        Assert.All(entries, e => Assert.Equal(HostFileEntryKind.Dataset, e.Kind));
        var proclib = entries.Single(e => e.Name == "SYS1.PROCLIB").Attributes!;
        Assert.Equal("PO", proclib.Dsorg);
        Assert.Equal("FB", proclib.Recfm);
        Assert.Equal(80, proclib.Lrecl);
        Assert.True(proclib.IsPartitioned);
        Assert.Equal("/zosmf/restfiles/ds?dslevel=SYS1.**", handler.Requests[1].Uri.PathAndQuery);
    }

    [Fact]
    public async Task Attributes_header_ignored_so_base_is_always_asked_for_and_a_whole_list_sets_no_limit()
    {
        var handler = new RecordedHandler().Then("login-200").Then("ds-list-sys1");
        using var service = Service(handler);

        await service.ListDatasetsAsync("SYS1.**", HostListRequest.All, TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Get);
        // mvsMF-compat: attributes-header-ignored — mvsMF always answers with attributes; z/OSMF only when asked.
        Assert.Equal("base", request.Headers["X-IBM-Attributes"]);
        Assert.DoesNotContain("X-IBM-Max-Items", request.HeaderNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("start=", request.Uri.Query);
        Assert.DoesNotContain("pattern=", request.Uri.Query);
    }

    [Fact]
    public async Task A_dataset_page_carries_the_limit_and_the_next_page_starts_after_its_last_name()
    {
        var handler = new RecordedHandler().Then("login-200")
            .Then(HttpStatusCode.OK, """{"items":[{"dsname":"A.B"},{"dsname":"A.C"}],"returnedRows":2,"moreRows":true}""")
            .Then(HttpStatusCode.OK, """{"items":[{"dsname":"A.C"},{"dsname":"A.D"},{"dsname":"A.E"}],"returnedRows":3,"moreRows":true}""")
            .Then(HttpStatusCode.OK, """{"items":[{"dsname":"A.E"},{"dsname":"A.F"}],"returnedRows":2}""");
        using var service = Service(handler);
        var ct = TestContext.Current.CancellationToken;

        var first = await service.ListDatasetsAsync("A.**", new HostListRequest(MaxItems: 2), ct);
        var second = await service.ListDatasetsAsync("A.**", new HostListRequest(MaxItems: 2, Continuation: first.Continuation), ct);
        var third = await service.ListDatasetsAsync("A.**", new HostListRequest(MaxItems: 2, Continuation: second.Continuation), ct);

        Assert.Equal(new[] { "A.B", "A.C" }, first.Entries.Select(e => e.Name));
        Assert.False(first.IsComplete);
        Assert.Equal("2", handler.Requests[1].Headers["X-IBM-Max-Items"]);
        Assert.DoesNotContain("start=", handler.Requests[1].Uri.Query);

        // start= is inclusive on mvsMF and z/OSMF, so a continued page asks for one more and drops the repeat.
        Assert.Equal(new[] { "A.D", "A.E" }, second.Entries.Select(e => e.Name));
        Assert.False(second.IsComplete);
        Assert.Equal("/zosmf/restfiles/ds?dslevel=A.**&start=A.C", handler.Requests[2].Uri.PathAndQuery);
        Assert.Equal("3", handler.Requests[2].Headers["X-IBM-Max-Items"]);

        Assert.Equal(new[] { "A.F" }, third.Entries.Select(e => e.Name));
        Assert.True(third.IsComplete);
        Assert.Equal("/zosmf/restfiles/ds?dslevel=A.**&start=A.E", handler.Requests[3].Uri.PathAndQuery);
    }

    [Fact]
    public async Task A_continued_page_missing_its_start_name_is_cut_to_the_page_size_and_continues()
    {
        // The name the page was to start at was deleted meanwhile: nothing to drop, so the extra item asked for
        // is real, and it is left for the next page rather than returned over the limit.
        var handler = new RecordedHandler().Then("login-200")
            .Then(HttpStatusCode.OK, """{"items":[{"dsname":"A.D"},{"dsname":"A.E"},{"dsname":"A.F"}],"returnedRows":3}""");
        using var service = Service(handler);

        var page = await service.ListDatasetsAsync("A.**", new HostListRequest(MaxItems: 2, Continuation: "A.C"), TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "A.D", "A.E" }, page.Entries.Select(e => e.Name));
        Assert.False(page.IsComplete);
    }

    [Fact]
    public async Task An_empty_dataset_list_is_complete()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("ds-list-empty"));
        var listing = await service.ListDatasetsAsync("NOSUCH.HLQ", HostListRequest.All, TestContext.Current.CancellationToken);
        Assert.Empty(listing.Entries);
        Assert.True(listing.IsComplete);
    }

    [Fact]
    public async Task A_partial_answer_to_a_whole_dataset_list_is_an_error()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then(HttpStatusCode.OK, """{"items":[{"dsname":"A.B"}],"moreRows":true}"""));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.ListDatasetsAsync("A.**", HostListRequest.All, TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.ServerError, ex.Kind);
        Assert.Equal("Dataset list: the host returned only part of the list.", ex.Message);
    }

    [Fact]
    public async Task The_filter_is_folded_and_its_hash_and_percent_are_escaped()
    {
        var handler = new RecordedHandler().Then("login-200").Then(HttpStatusCode.OK, """{"items":[],"moreRows":false}""");
        using var service = Service(handler);

        await service.ListDatasetsAsync(" sys1.#a% ", HostListRequest.All, TestContext.Current.CancellationToken);

        Assert.Equal("/zosmf/restfiles/ds?dslevel=SYS1.%23A%25", handler.Requests[1].Uri.PathAndQuery);
    }

    [Fact]
    public async Task A_bad_filter_is_refused_without_a_request()
    {
        var handler = new RecordedHandler();
        using var service = Service(handler);

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.ListDatasetsAsync("SYS1.A B", HostListRequest.All, TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.InvalidRequest, ex.Kind);
        Assert.Equal("A filter cannot contain ' '.", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Numeric_attributes_and_nameless_items_are_tolerated()
    {
        var handler = new RecordedHandler().Then("login-200").Then(HttpStatusCode.OK,
            """{"items":[{"dsname":"A.B","dsorg":"PS","recfm":"VB","lrecl":255,"blksz":6233,"vol":"V1"},{"dsorg":"PO"},{"dsname":"C.D","lrecl":"","dsorg":""}]}""");
        using var service = Service(handler);

        var entries = (await service.ListDatasetsAsync("A.**", HostListRequest.All, TestContext.Current.CancellationToken)).Entries;

        Assert.Equal(new[] { "A.B", "C.D" }, entries.Select(e => e.Name));
        Assert.Equal(new DatasetAttributes("PS", "VB", 255, 6233, "V1"), entries[0].Attributes);
        Assert.Equal(new DatasetAttributes(null, null, null, null, null), entries[1].Attributes);
    }

    [Fact]
    public async Task Member_list_reads_every_name()
    {
        var handler = new RecordedHandler().Then("login-200").Then("members-proclib");
        using var service = Service(handler);

        var listing = await service.ListMembersAsync(HostPath.ForDataset("SYS1.PROCLIB"), HostListRequest.All, TestContext.Current.CancellationToken);
        var members = listing.Entries;

        Assert.True(listing.IsComplete);
        Assert.Equal("ASMFC", members[0].Name);
        Assert.Contains(members, m => m.Name == "JES2");
        Assert.All(members, m => Assert.Equal(HostFileEntryKind.Member, m.Kind));
        Assert.All(members, m => Assert.Null(m.Attributes));
        Assert.Equal("/zosmf/restfiles/ds/SYS1.PROCLIB/member", handler.Requests[1].Uri.PathAndQuery);
    }

    [Fact]
    public async Task A_whole_member_list_sets_no_limit_and_no_pattern()
    {
        var handler = new RecordedHandler().Then("login-200").Then("members-proclib");
        using var service = Service(handler);

        await service.ListMembersAsync(HostPath.ForDataset("SYS1.PROCLIB"), HostListRequest.All, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("X-IBM-Max-Items", handler.Requests[1].HeaderNames, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("", handler.Requests[1].Uri.Query);
    }

    [Fact]
    public async Task A_recorded_member_page_carries_its_continuation()
    {
        var handler = new RecordedHandler().Then("login-200").Then("members-maclib-page");
        using var service = Service(handler);

        var page = await service.ListMembersAsync(HostPath.ForDataset("SYS1.MACLIB"), new HostListRequest(MaxItems: 3), TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "ABEND", "ACB", "ACBVS" }, page.Entries.Select(e => e.Name));
        Assert.False(page.IsComplete);
        Assert.Equal("3", handler.Requests[1].Headers["X-IBM-Max-Items"]);
    }

    [Fact]
    public async Task A_member_pattern_goes_to_the_host_with_its_percent_escaped()
    {
        var handler = new RecordedHandler().Then("login-200").Then(HttpStatusCode.OK, """{"items":[{"member":"JES2"}],"returnedRows":1}""");
        using var service = Service(handler);

        var listing = await service.ListMembersAsync(HostPath.ForDataset("SYS1.PROCLIB"), new HostListRequest(NamePattern: "jes%*"), TestContext.Current.CancellationToken);

        Assert.Equal("/zosmf/restfiles/ds/SYS1.PROCLIB/member?pattern=JES%25*", handler.Requests[1].Uri.PathAndQuery);
        Assert.Equal(new[] { "JES2" }, listing.Entries.Select(e => e.Name));
    }

    [Fact]
    public async Task A_member_page_continues_after_its_last_name_with_the_pattern_kept()
    {
        var handler = new RecordedHandler().Then("login-200")
            .Then(HttpStatusCode.OK, """{"items":[{"member":"JES2"},{"member":"JES2BLD"}],"returnedRows":2,"moreRows":true}""")
            .Then(HttpStatusCode.OK, """{"items":[{"member":"JES2BLD"},{"member":"JES2JOB"}],"returnedRows":2}""");
        using var service = Service(handler);
        var ct = TestContext.Current.CancellationToken;
        var pds = HostPath.ForDataset("SYS1.PROCLIB");

        var first = await service.ListMembersAsync(pds, new HostListRequest(MaxItems: 2, NamePattern: "JES2*"), ct);
        var second = await service.ListMembersAsync(pds, new HostListRequest(MaxItems: 2, Continuation: first.Continuation, NamePattern: "JES2*"), ct);

        Assert.Equal(new[] { "JES2", "JES2BLD" }, first.Entries.Select(e => e.Name));
        Assert.False(first.IsComplete);
        Assert.Equal("2", handler.Requests[1].Headers["X-IBM-Max-Items"]);
        Assert.Equal("/zosmf/restfiles/ds/SYS1.PROCLIB/member?pattern=JES2*", handler.Requests[1].Uri.PathAndQuery);
        Assert.Equal(new[] { "JES2JOB" }, second.Entries.Select(e => e.Name));
        Assert.True(second.IsComplete);
        Assert.Equal("/zosmf/restfiles/ds/SYS1.PROCLIB/member?pattern=JES2*&start=JES2BLD", handler.Requests[2].Uri.PathAndQuery);
        Assert.Equal("3", handler.Requests[2].Headers["X-IBM-Max-Items"]);
    }

    [Fact]
    public async Task A_missing_dataset_has_no_member_list()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("members-missing-dataset"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() =>
            service.ListMembersAsync(HostPath.ForDataset("MVSCE02.NOSUCH"), HostListRequest.All, TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);
        Assert.Equal("MVSCE02.NOSUCH: not found.", ex.Message);
    }

    [Fact]
    public async Task A_sequential_dataset_has_no_member_list()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("members-not-partitioned"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() =>
            service.ListMembersAsync(HostPath.ForDataset("MVSCE02.LIZT.SAMPLIB2.XMIT"), HostListRequest.All, TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.InvalidRequest, ex.Kind);
        Assert.StartsWith("MVSCE02.LIZT.SAMPLIB2.XMIT: the host refused the request (Dataset is not partitioned", ex.Message);
    }

    [Fact]
    public async Task A_partial_answer_to_a_whole_member_list_is_an_error()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then(HttpStatusCode.OK, """{"items":[{"member":"A"}],"moreRows":true}"""));

        var ex = await Assert.ThrowsAsync<HostFileException>(() =>
            service.ListMembersAsync(HostPath.ForDataset("SYS1.MACLIB"), HostListRequest.All, TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.ServerError, ex.Kind);
        Assert.Equal("SYS1.MACLIB: the host returned only part of the list.", ex.Message);
    }

    [Fact]
    public async Task A_member_path_cannot_be_listed()
    {
        using var service = Service(new RecordedHandler());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ListMembersAsync(HostPath.ForMember("SYS1.PROCLIB", "JES2"), HostListRequest.All, TestContext.Current.CancellationToken));
    }
}
