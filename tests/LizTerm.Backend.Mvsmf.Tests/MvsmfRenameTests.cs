// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfRenameTests
{
    private static readonly HostPath One = HostPath.ForMember("IBMUSER.LIZITEST.FIX", "ONE");
    private static readonly HostPath Fix = HostPath.ForDataset("IBMUSER.LIZITEST.FIX");

    private static MvsmfFileService Service(RecordedHandler handler) =>
        new(handler, MvsmfAuthTests.Base, MvsmfAuthTests.Providing([], new HostCredentials("MVSCE02", "pw")));

    [Fact]
    public async Task Put_json_is_rename_so_a_member_rename_puts_json_to_the_new_name()
    {
        var handler = new RecordedHandler().Then("login-200").Then("rename-member-204");
        using var service = Service(handler);

        await service.RenameAsync(One, "two", TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Put);
        Assert.Equal("/zosmf/restfiles/ds/IBMUSER.LIZITEST.FIX(TWO)", request.Uri.PathAndQuery);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("{\"request\":\"rename\",\"from-dataset\":{\"dsn\":\"IBMUSER.LIZITEST.FIX\",\"member\":\"ONE\"}}", request.BodyText);
        Assert.DoesNotContain("X-IBM-Data-Type", request.HeaderNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_dataset_rename_names_only_the_old_dataset()
    {
        var handler = new RecordedHandler().Then("login-200").Then("rename-ds-204");
        using var service = Service(handler);

        await service.RenameAsync(Fix, "ibmuser.lizitest.fix2", TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Put);
        Assert.Equal("/zosmf/restfiles/ds/IBMUSER.LIZITEST.FIX2", request.Uri.PathAndQuery);
        Assert.Equal("{\"request\":\"rename\",\"from-dataset\":{\"dsn\":\"IBMUSER.LIZITEST.FIX\"}}", request.BodyText);
    }

    [Fact]
    public async Task Renaming_a_missing_member_is_not_found()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("rename-member-missing"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.RenameAsync(One, "TWO", TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);
        Assert.Equal("Rename IBMUSER.LIZITEST.FIX(ONE) to TWO: not found.", ex.Message);
    }

    [Fact]
    public async Task Rename_target_exists_400_is_already_exists()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("rename-member-exists"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.RenameAsync(One, "TWO", TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.AlreadyExists, ex.Kind);
        Assert.Equal("Rename IBMUSER.LIZITEST.FIX(ONE) to TWO: a member of that name already exists.", ex.Message);
    }

    [Theory]
    [InlineData("TOOLONGNAME")]
    [InlineData("")]
    [InlineData("1BAD")]
    public async Task A_new_member_name_the_rules_refuse_is_never_sent(string newName)
    {
        var handler = new RecordedHandler();
        using var service = Service(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => service.RenameAsync(One, newName, TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_new_dataset_name_the_rules_refuse_is_never_sent()
    {
        var handler = new RecordedHandler();
        using var service = Service(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => service.RenameAsync(Fix, "A.B(C)", TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_repeat_after_a_401_sends_the_same_body()
    {
        var handler = new RecordedHandler()
            .Then("login-200").Then(System.Net.HttpStatusCode.Unauthorized).Then("login-200").Then("rename-member-204");
        using var service = Service(handler);

        await service.RenameAsync(One, "TWO", TestContext.Current.CancellationToken);

        var puts = handler.Requests.Where(r => r.Method == HttpMethod.Put).ToList();
        Assert.Equal(2, puts.Count);
        Assert.Equal(puts[0].Body, puts[1].Body);
    }
}
