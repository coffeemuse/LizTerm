// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfListTests
{
    private static MvsmfFileService Service(RecordedHandler handler) =>
        new(handler, MvsmfAuthTests.Base, MvsmfAuthTests.Answering([], new HostCredentials("MVSCE02", "pw")));

    [Fact]
    public async Task Dataset_list_reads_names_and_attributes()
    {
        var handler = new RecordedHandler().Then("ds-list-sys1");
        using var service = Service(handler);

        var entries = await service.ListDatasetsAsync("sys1.**", TestContext.Current.CancellationToken);

        Assert.True(entries.Count > 3);
        Assert.Equal("SYS1.ACMDLIB", entries[0].Name);
        Assert.All(entries, e => Assert.Equal(HostFileEntryKind.Dataset, e.Kind));
        var proclib = entries.Single(e => e.Name == "SYS1.PROCLIB").Attributes!;
        Assert.Equal("PO", proclib.Dsorg);
        Assert.Equal("FB", proclib.Recfm);
        Assert.Equal(80, proclib.Lrecl);
        Assert.True(proclib.IsPartitioned);
        Assert.Equal("/zosmf/restfiles/ds?dslevel=SYS1.**", handler.Requests[0].Uri.PathAndQuery);
    }

    [Fact]
    public async Task Dataset_list_ignores_start_so_the_whole_list_is_asked_for()
    {
        var handler = new RecordedHandler().Then("ds-list-sys1");
        using var service = Service(handler);

        await service.ListDatasetsAsync("SYS1.**", TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.DoesNotContain("X-IBM-Max-Items", request.HeaderNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("start=", request.Uri.Query);
    }

    [Fact]
    public async Task Dataset_list_morerows_false_is_a_complete_empty_list()
    {
        using var service = Service(new RecordedHandler().Then("ds-list-empty"));
        Assert.Empty(await service.ListDatasetsAsync("NOSUCH.HLQ", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_filter_is_folded_and_its_hash_and_percent_are_escaped()
    {
        var handler = new RecordedHandler().Then(HttpStatusCode.OK, """{"items":[],"moreRows":false}""");
        using var service = Service(handler);

        await service.ListDatasetsAsync(" sys1.#a% ", TestContext.Current.CancellationToken);

        Assert.Equal("/zosmf/restfiles/ds?dslevel=SYS1.%23A%25", handler.Requests[0].Uri.PathAndQuery);
    }

    [Fact]
    public async Task A_bad_filter_is_refused_without_a_request()
    {
        var handler = new RecordedHandler();
        using var service = Service(handler);

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.ListDatasetsAsync("SYS1.A B", TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.InvalidRequest, ex.Kind);
        Assert.Equal("A filter cannot contain ' '.", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Numeric_attributes_and_nameless_items_are_tolerated()
    {
        var handler = new RecordedHandler().Then(HttpStatusCode.OK,
            """{"items":[{"dsname":"A.B","dsorg":"PS","recfm":"VB","lrecl":255,"blksz":6233,"vol":"V1"},{"dsorg":"PO"},{"dsname":"C.D","lrecl":"","dsorg":""}]}""");
        using var service = Service(handler);

        var entries = await service.ListDatasetsAsync("A.**", TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "A.B", "C.D" }, entries.Select(e => e.Name));
        Assert.Equal(new DatasetAttributes("PS", "VB", 255, 6233, "V1"), entries[0].Attributes);
        Assert.Equal(new DatasetAttributes(null, null, null, null, null), entries[1].Attributes);
    }

    [Fact]
    public async Task Member_list_reads_every_name()
    {
        var handler = new RecordedHandler().Then("members-proclib");
        using var service = Service(handler);

        var members = await service.ListMembersAsync(HostPath.ForDataset("SYS1.PROCLIB"), TestContext.Current.CancellationToken);

        Assert.Equal("ASMFC", members[0].Name);
        Assert.Contains(members, m => m.Name == "JES2");
        Assert.All(members, m => Assert.Equal(HostFileEntryKind.Member, m.Kind));
        Assert.All(members, m => Assert.Null(m.Attributes));
        Assert.Equal("/zosmf/restfiles/ds/SYS1.PROCLIB/member", handler.Requests[0].Uri.PathAndQuery);
    }

    [Fact]
    public async Task Member_list_ignores_max_items_so_none_is_sent()
    {
        var handler = new RecordedHandler().Then("members-proclib");
        using var service = Service(handler);

        await service.ListMembersAsync(HostPath.ForDataset("SYS1.PROCLIB"), TestContext.Current.CancellationToken);

        Assert.DoesNotContain("X-IBM-Max-Items", handler.Requests[0].HeaderNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Member_list_empty_for_missing_dataset_is_not_an_error()
    {
        using var service = Service(new RecordedHandler().Then("members-missing-dataset"));
        Assert.Empty(await service.ListMembersAsync(HostPath.ForDataset("MVSCE02.NOSUCH"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_member_path_cannot_be_listed()
    {
        using var service = Service(new RecordedHandler());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ListMembersAsync(HostPath.ForMember("SYS1.PROCLIB", "JES2"), TestContext.Current.CancellationToken));
    }
}
