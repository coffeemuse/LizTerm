// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Tests.Fakes;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.HostFiles;

/// <summary>The fake is what every browser test stands on, so its own contract is pinned.</summary>
public class FakeHostFileServiceTests
{
    [Fact]
    public async Task Writes_add_members_and_deletes_remove_them()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new FakeHostFileService();
        host.AddDataset("A.CNTL", members: ["ONE"]);
        var two = HostPath.ForMember("A.CNTL", "TWO");

        await host.WriteTextAsync(two, ["X"], cancellationToken: token);
        Assert.Equal(new[] { "ONE", "TWO" }, host.Members["A.CNTL"]);
        Assert.Equal(new[] { "X" }, (await host.ReadTextAsync(two, cancellationToken: token)).Lines);

        await host.DeleteAsync(two, token);
        Assert.Equal(new[] { "ONE" }, host.Members["A.CNTL"]);
        var ex = await Assert.ThrowsAsync<HostFileException>(() => host.ReadTextAsync(two, cancellationToken: token));
        Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);
        Assert.Equal(new[] { "writetext:A.CNTL(TWO):1", "readtext:A.CNTL(TWO)", "delete:A.CNTL(TWO)", "readtext:A.CNTL(TWO)" }, host.CallsSnapshot());
    }

    [Fact]
    public async Task A_store_transform_alters_what_a_text_write_keeps()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new FakeHostFileService { StoreTransform = (path, lines) => [path, .. lines.Skip(1)] };
        host.AddDataset("A.CNTL");
        var one = HostPath.ForMember("A.CNTL", "ONE");

        await host.WriteTextAsync(one, ["X", "Y"], cancellationToken: token);

        Assert.Equal(new[] { "A.CNTL(ONE)", "Y" }, host.Text["A.CNTL(ONE)"]);
        Assert.Contains("writetext:A.CNTL(ONE):2", host.CallsSnapshot());
    }

    [Fact]
    public async Task Writes_stamp_their_target_and_a_stale_stamp_is_a_conflict()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new FakeHostFileService();
        host.AddDataset("A.CNTL");
        var one = HostPath.ForMember("A.CNTL", "ONE");

        var first = await host.WriteTextAsync(one, ["X"], cancellationToken: token);
        var read = await host.ReadTextAsync(one, withEtag: true, cancellationToken: token);
        var unasked = await host.ReadTextAsync(one, cancellationToken: token);
        var second = await host.WriteTextAsync(one, ["Y"], ifMatch: first, token);

        Assert.Equal("stamp-1", first);
        Assert.Equal("stamp-1", read.Etag);
        Assert.Null(unasked.Etag);
        Assert.Equal("stamp-2", second);
        var ex = await Assert.ThrowsAsync<HostFileException>(() => host.WriteTextAsync(one, ["Z"], ifMatch: first, token));
        Assert.Equal(HostFileErrorKind.Conflict, ex.Kind);
        Assert.Equal(new[] { "Y" }, host.Text["A.CNTL(ONE)"]);
        Assert.Equal(new string?[] { null, "stamp-1", "stamp-1" }, host.IfMatches);
    }

    [Fact]
    public async Task A_create_adds_the_dataset_and_a_second_create_cannot_allocate()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new FakeHostFileService();
        var pds = HostPath.ForDataset("A.NEW");
        var allocation = new DatasetAllocation(DatasetOrganization.Partitioned, "FB", 80, 3120, SpaceUnit.Tracks, 1, 1, 2);

        await host.CreateDatasetAsync(pds, allocation, token);

        var entry = Assert.Single(host.Datasets);
        Assert.Equal("A.NEW", entry.Name);
        Assert.Equal("PO", entry.Attributes!.Dsorg);
        Assert.Equal("FB", entry.Attributes.Recfm);
        Assert.Equal(80, entry.Attributes.Lrecl);
        Assert.Equal(3120, entry.Attributes.Blksize);
        Assert.Empty(host.Members["A.NEW"]);
        var ex = await Assert.ThrowsAsync<HostFileException>(() => host.CreateDatasetAsync(pds, allocation, token));
        Assert.Equal(HostFileErrorKind.CannotAllocate, ex.Kind);
        Assert.Equal(new[] { "create:A.NEW", "create:A.NEW" }, host.CallsSnapshot());
    }

    [Fact]
    public async Task A_create_or_rename_the_local_rules_refuse_is_thrown_before_it_is_logged()
    {
        // As the backend, which refuses both before anything is sent.
        var token = TestContext.Current.CancellationToken;
        var host = new FakeHostFileService();
        host.AddDataset("A.CNTL", members: ["ONE"]);
        var allocation = new DatasetAllocation(DatasetOrganization.Partitioned, "FB", 80, 3120, SpaceUnit.Tracks, 1, 1, 2);

        await Assert.ThrowsAsync<ArgumentException>(() => host.CreateDatasetAsync(HostPath.ForDataset("A.NEW"), allocation with { Primary = 0 }, token));
        await Assert.ThrowsAsync<ArgumentException>(() => host.CreateDatasetAsync(HostPath.ForMember("A.NEW", "X"), allocation, token));
        await Assert.ThrowsAsync<ArgumentException>(() => host.RenameAsync(HostPath.ForMember("A.CNTL", "ONE"), "TOOLONGNAME", token));
        await Assert.ThrowsAsync<ArgumentException>(() => host.RenameAsync(HostPath.ForDataset("A.CNTL"), "A.B(C)", token));

        Assert.Empty(host.CallsSnapshot());
        Assert.Equal(new[] { "ONE" }, host.Members["A.CNTL"]);
    }

    [Fact]
    public async Task Deleting_what_is_not_there_is_not_found()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new FakeHostFileService();
        host.AddDataset("A.CNTL", members: ["ONE"]);

        var member = await Assert.ThrowsAsync<HostFileException>(() => host.DeleteAsync(HostPath.ForMember("A.CNTL", "TWO"), token));
        var dataset = await Assert.ThrowsAsync<HostFileException>(() => host.DeleteAsync(HostPath.ForDataset("A.GONE"), token));

        Assert.Equal((HostFileErrorKind.NotFound, 5), (member.Kind, member.Reason));
        Assert.Equal((HostFileErrorKind.NotFound, 4), (dataset.Kind, dataset.Reason));
        Assert.Equal(new[] { "ONE" }, host.Members["A.CNTL"]);
        Assert.Equal(new[] { "delete:A.CNTL(TWO)", "delete:A.GONE" }, host.CallsSnapshot());
    }

    [Fact]
    public async Task A_dataset_rename_onto_an_existing_name_is_the_hosts_500()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new FakeHostFileService();
        host.AddDataset("A.CNTL", members: ["ONE"]);
        host.AddDataset("A.JCL", members: ["TWO"]);

        var ex = await Assert.ThrowsAsync<HostFileException>(() => host.RenameAsync(HostPath.ForDataset("A.CNTL"), "A.JCL", token));

        Assert.Equal((HostFileErrorKind.ServerError, 8), (ex.Kind, ex.Reason));
        Assert.Equal("Rename A.CNTL to A.JCL: Rename operation failed (reason 8).", ex.Message);
        Assert.Equal(new[] { "A.CNTL", "A.JCL" }, host.Datasets.Select(d => d.Name));
        Assert.Equal(new[] { "ONE" }, host.Members["A.CNTL"]);
    }

    [Fact]
    public async Task A_member_rename_moves_the_member_its_content_and_its_stamp()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new FakeHostFileService();
        host.AddDataset("A.CNTL", members: ["ONE", "TWO"]);
        var one = HostPath.ForMember("A.CNTL", "ONE");
        await host.WriteTextAsync(one, ["X"], cancellationToken: token);

        await host.RenameAsync(one, "THREE", token);

        Assert.Equal(new[] { "THREE", "TWO" }, host.Members["A.CNTL"]);
        Assert.Equal(new[] { "X" }, host.Text["A.CNTL(THREE)"]);
        Assert.Equal("stamp-1", host.Etags["A.CNTL(THREE)"]);
        Assert.False(host.Text.ContainsKey("A.CNTL(ONE)"));
        var missing = await Assert.ThrowsAsync<HostFileException>(() => host.RenameAsync(one, "FOUR", token));
        Assert.Equal(HostFileErrorKind.NotFound, missing.Kind);
        var exists = await Assert.ThrowsAsync<HostFileException>(() => host.RenameAsync(HostPath.ForMember("A.CNTL", "THREE"), "TWO", token));
        Assert.Equal(HostFileErrorKind.AlreadyExists, exists.Kind);
        Assert.Contains("rename:A.CNTL(ONE):THREE", host.CallsSnapshot());
    }

    [Fact]
    public async Task A_dataset_rename_and_delete_carry_everything_under_it()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new FakeHostFileService();
        host.AddDataset("A.CNTL", members: ["ONE"]);
        await host.WriteTextAsync(HostPath.ForMember("A.CNTL", "ONE"), ["X"], cancellationToken: token);

        await host.RenameAsync(HostPath.ForDataset("A.CNTL"), "A.JCL", token);

        Assert.Equal("A.JCL", Assert.Single(host.Datasets).Name);
        Assert.Equal(new[] { "ONE" }, host.Members["A.JCL"]);
        Assert.Equal(new[] { "X" }, host.Text["A.JCL(ONE)"]);
        Assert.Equal("stamp-1", host.Etags["A.JCL(ONE)"]);
        Assert.False(host.Members.ContainsKey("A.CNTL"));

        await host.DeleteAsync(HostPath.ForDataset("A.JCL"), token);

        Assert.Empty(host.Datasets);
        Assert.Empty(host.Members);
        Assert.Empty(host.Text);
        Assert.Empty(host.Etags);
        var gone = await Assert.ThrowsAsync<HostFileException>(() => host.RenameAsync(HostPath.ForDataset("A.JCL"), "A.X", token));
        Assert.Equal(HostFileErrorKind.NotFound, gone.Kind);
    }

    [Fact]
    public async Task A_failure_is_thrown_by_the_matching_call()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new FakeHostFileService();
        host.Failures["list:SYS1.**"] = new HostFileException(HostFileErrorKind.Unreachable, "down");
        await Assert.ThrowsAsync<HostFileException>(() => host.ListDatasetsAsync("SYS1.**", HostListRequest.All, token));
        Assert.Empty((await host.ListDatasetsAsync("OTHER.**", HostListRequest.All, token)).Entries);
    }

    [Fact]
    public async Task The_gate_holds_calls_and_counts_how_many_ran_at_once()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new FakeHostFileService { Gate = new TaskCompletionSource() };
        host.Text["A.B"] = ["x"];
        var a = host.ReadTextAsync(HostPath.ForDataset("A.B"), cancellationToken: token);
        var b = host.ReadTextAsync(HostPath.ForDataset("A.B"), cancellationToken: token);
        host.Gate.SetResult();
        await Task.WhenAll(a, b);
        Assert.Equal(2, host.MaxConcurrent);
    }

    [Fact]
    public async Task A_dataset_listing_is_narrowed_by_its_pattern()
    {
        var token = TestContext.Current.CancellationToken;
        var host = new FakeHostFileService();
        host.AddDataset("A.CNTL");
        host.AddDataset("A.JCL");
        host.AddDataset("A.X.CNTL");
        host.AddDataset("B.CNTL");

        Assert.Equal(new[] { "A.CNTL", "A.JCL", "A.X.CNTL" }, (await host.ListDatasetsAsync("A.**", HostListRequest.All, token)).Entries.Select(e => e.Name));
        Assert.Equal(new[] { "A.CNTL", "A.JCL" }, (await host.ListDatasetsAsync("a.*", HostListRequest.All, token)).Entries.Select(e => e.Name));
        Assert.Equal(new[] { "A.CNTL", "B.CNTL" }, (await host.ListDatasetsAsync("%.CNTL", HostListRequest.All, token)).Entries.Select(e => e.Name));
        Assert.Empty((await host.ListDatasetsAsync("OTHER.**", HostListRequest.All, token)).Entries);
    }
}
