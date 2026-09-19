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

        await host.WriteTextAsync(two, ["X"], token);
        Assert.Equal(new[] { "ONE", "TWO" }, host.Members["A.CNTL"]);
        Assert.Equal(new[] { "X" }, await host.ReadTextAsync(two, cancellationToken: token));

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

        await host.WriteTextAsync(one, ["X", "Y"], token);

        Assert.Equal(new[] { "A.CNTL(ONE)", "Y" }, host.Text["A.CNTL(ONE)"]);
        Assert.Contains("writetext:A.CNTL(ONE):2", host.CallsSnapshot());
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
}
