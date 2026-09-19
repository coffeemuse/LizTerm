// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfDeleteTests
{
    private static readonly HostPath NewMember = HostPath.ForMember("MVSCE02.CNTL", "NEWMEM");
    private static readonly HostPath Fix2 = HostPath.ForDataset("IBMUSER.LIZITEST.FIX2");

    private static MvsmfFileService Service(RecordedHandler handler) =>
        new(handler, MvsmfAuthTests.Base, MvsmfAuthTests.Providing([], new HostCredentials("MVSCE02", "pw")));

    [Fact]
    public async Task A_member_is_deleted()
    {
        var handler = new RecordedHandler().Then("login-200").Then("delete-204");
        using var service = Service(handler);

        await service.DeleteAsync(NewMember, TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Delete);
        Assert.Equal("/zosmf/restfiles/ds/MVSCE02.CNTL(NEWMEM)", request.Uri.PathAndQuery);
    }

    [Fact]
    public async Task Deleting_a_missing_member_is_not_found()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("delete-missing"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.DeleteAsync(NewMember, TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);
        Assert.Equal("MVSCE02.CNTL(NEWMEM): not found.", ex.Message);
    }

    [Fact]
    public async Task A_whole_dataset_is_deleted()
    {
        var handler = new RecordedHandler().Then("login-200").Then("delete-ds-204");
        using var service = Service(handler);

        await service.DeleteAsync(Fix2, TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Delete);
        Assert.Equal("/zosmf/restfiles/ds/IBMUSER.LIZITEST.FIX2", request.Uri.PathAndQuery);
    }

    [Fact]
    public async Task Deleting_a_missing_dataset_is_not_found()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("delete-ds-missing"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.DeleteAsync(Fix2, TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);
        Assert.Equal(4, ex.Reason);
        Assert.Equal("IBMUSER.LIZITEST.FIX2: not found.", ex.Message);
    }
}
