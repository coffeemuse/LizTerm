// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfCreateTests
{
    private static readonly HostPath NewPds = HostPath.ForDataset("IBMUSER.LIZITEST.FIX");
    private static readonly DatasetAllocation Pds = new(DatasetOrganization.Partitioned, "fb", 80, 3120, SpaceUnit.Tracks, 1, 1, 2);

    private static MvsmfFileService Service(RecordedHandler handler) =>
        new(handler, MvsmfAuthTests.Base, MvsmfAuthTests.Providing([], new HostCredentials("MVSCE02", "pw")));

    [Fact]
    public async Task A_create_posts_the_allocation_as_json()
    {
        var handler = new RecordedHandler().Then("login-200").Then("create-201");
        using var service = Service(handler);

        await service.CreateDatasetAsync(NewPds, Pds, TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.Contains("restfiles"));
        Assert.Equal("/zosmf/restfiles/ds/IBMUSER.LIZITEST.FIX", request.Uri.PathAndQuery);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("{\"dsorg\":\"PO\",\"recfm\":\"FB\",\"lrecl\":80,\"blksize\":3120,\"alcunit\":\"TRK\",\"primary\":1,\"secondary\":1,\"dirblk\":2}", request.BodyText);
    }

    [Fact]
    public async Task A_sequential_create_sends_no_directory_blocks_and_cylinders_when_asked()
    {
        var handler = new RecordedHandler().Then("login-200").Then("create-201");
        using var service = Service(handler);

        await service.CreateDatasetAsync(NewPds, Pds with { Organization = DatasetOrganization.Sequential, Unit = SpaceUnit.Cylinders, DirectoryBlocks = 0 }, TestContext.Current.CancellationToken);

        Assert.Equal("{\"dsorg\":\"PS\",\"recfm\":\"FB\",\"lrecl\":80,\"blksize\":3120,\"alcunit\":\"CYL\",\"primary\":1,\"secondary\":1}", handler.Requests[1].BodyText);
    }

    [Fact]
    public async Task Create_failure_is_one_500_reported_as_cannot_allocate()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("create-dynalloc-500"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.CreateDatasetAsync(NewPds, Pds, TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.CannotAllocate, ex.Kind);
        Assert.StartsWith("IBMUSER.LIZITEST.FIX: the host could not allocate it", ex.Message);
    }

    [Fact]
    public async Task An_allocation_that_fails_its_own_rules_is_refused_before_sending()
    {
        var handler = new RecordedHandler();
        using var service = Service(handler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateDatasetAsync(NewPds, Pds with { Primary = 0 }, TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_member_path_cannot_be_created()
    {
        var handler = new RecordedHandler();
        using var service = Service(handler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateDatasetAsync(HostPath.ForMember("A.B", "C"), Pds, TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_repeat_after_a_401_posts_the_same_body()
    {
        var handler = new RecordedHandler()
            .Then("login-200").Then(System.Net.HttpStatusCode.Unauthorized).Then("login-200").Then("create-201");
        using var service = Service(handler);

        await service.CreateDatasetAsync(NewPds, Pds, TestContext.Current.CancellationToken);

        var posts = handler.Requests.Where(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.Contains("restfiles")).ToList();
        Assert.Equal(2, posts.Count);
        Assert.Equal(posts[0].Body, posts[1].Body);
    }
}
