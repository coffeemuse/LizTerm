// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Text;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfErrorsTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, null, null, null, HostFileErrorKind.Unauthenticated)]
    [InlineData(HttpStatusCode.InternalServerError, 6, 8, 3, HostFileErrorKind.ServerError)]
    [InlineData(HttpStatusCode.NotFound, 6, 8, 5, HostFileErrorKind.NotFound)]
    [InlineData(HttpStatusCode.NotFound, 6, 8, 4, HostFileErrorKind.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, 6, 8, 4, HostFileErrorKind.NotFound)]
    [InlineData(HttpStatusCode.NotFound, 4, 6, 7, HostFileErrorKind.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, 4, 8, 0, HostFileErrorKind.NotAuthorized)]
    [InlineData(HttpStatusCode.Forbidden, null, null, null, HostFileErrorKind.NotAuthorized)]
    [InlineData(HttpStatusCode.BadRequest, 6, 8, 1, HostFileErrorKind.InvalidRequest)]
    [InlineData(HttpStatusCode.InternalServerError, 8, 900, 7, HostFileErrorKind.CannotAllocate)]
    [InlineData(HttpStatusCode.PreconditionFailed, 6, 8, 10, HostFileErrorKind.Conflict)]
    [InlineData(HttpStatusCode.PreconditionFailed, null, null, null, HostFileErrorKind.Conflict)]
    [InlineData(HttpStatusCode.BadRequest, 6, 8, 7, HostFileErrorKind.AlreadyExists)]
    [InlineData(HttpStatusCode.BadRequest, 6, 8, 3, HostFileErrorKind.InvalidRequest)]
    [InlineData(HttpStatusCode.InternalServerError, 6, 8, 8, HostFileErrorKind.ServerError)]
    [InlineData(HttpStatusCode.InternalServerError, 6, 8, 11, HostFileErrorKind.ServerError)]
    [InlineData(HttpStatusCode.BadGateway, null, null, null, HostFileErrorKind.ServerError)]
    public void Classifies_by_reason_before_status(HttpStatusCode status, int? category, int? rc, int? reason, HostFileErrorKind expected) =>
        Assert.Equal(expected, MvsmfErrors.Classify(status, category, rc, reason));

    [Fact]
    public async Task A_missing_member_is_not_found()
    {
        using var response = Fixture.Load("read-missing-member");
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        var ex = MvsmfErrors.FromResponse(response.StatusCode, body, "SYS1.PROCLIB(NOSUCHMB)");

        Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);
        Assert.Equal(5, ex.Reason);
        Assert.Equal("PDS member not found", ex.ServerMessage);
        Assert.Equal("SYS1.PROCLIB(NOSUCHMB): not found.", ex.Message);
    }

    [Fact]
    public async Task Text_write_truncates_so_the_500_is_a_server_error_quoting_the_host()
    {
        using var response = Fixture.Load("write-truncated");
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        var ex = MvsmfErrors.FromResponse(response.StatusCode, body, "MVSCE02.CNTL(LIZTEST)");

        Assert.Equal(HostFileErrorKind.ServerError, ex.Kind);
        Assert.Equal(3, ex.Reason);
        Assert.Equal("MVSCE02.CNTL(LIZTEST): Record truncated to the record length of the data set (reason 3).", ex.Message);
    }

    [Fact]
    public async Task A_pds_read_as_sequential_is_an_invalid_request_quoting_the_host()
    {
        using var response = Fixture.Load("read-pds-as-sequential");
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        var ex = MvsmfErrors.FromResponse(response.StatusCode, body, "SYS1.PROCLIB");

        Assert.Equal(HostFileErrorKind.InvalidRequest, ex.Kind);
        Assert.Equal(1, ex.Reason);
        Assert.StartsWith("SYS1.PROCLIB: the host refused the request (Dataset is a partitioned dataset", ex.Message);
    }

    [Fact]
    public async Task A_second_delete_is_not_found()
    {
        using var response = Fixture.Load("delete-missing");
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        var ex = MvsmfErrors.FromResponse(response.StatusCode, body, "X.Y(Z)");

        Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);
        Assert.Equal(5, ex.Reason);
        Assert.Equal("X.Y(Z): not found.", ex.Message);
    }

    [Fact]
    public void Cannot_open_is_500_so_the_message_tells_an_open_failure_from_a_write_failure()
    {
        var body = Encoding.UTF8.GetBytes("""{"rc":8,"category":6,"reason":3,"message":"Cannot open dataset for writing"}""");
        var ex = MvsmfErrors.FromResponse(HttpStatusCode.InternalServerError, body, "MVSCE02.CNTL(X)");
        Assert.Equal(HostFileErrorKind.CannotOpen, ex.Kind);
        Assert.Equal(3, ex.Reason);
        Assert.Equal("Cannot open dataset for writing", ex.ServerMessage);
        Assert.Equal("MVSCE02.CNTL(X): Cannot open dataset for writing.", ex.Message);
    }

    [Theory]
    [InlineData("", "Server information: server error (HTTP 502).")]
    [InlineData("<html>gateway</html>", "Server information: server error (HTTP 502).")]
    [InlineData("""{"rc":8,"category":9,"reason":12,"message":"odd"}""", "Server information: odd (reason 12).")]
    [InlineData("""{"rc":8,"category":6,"message":"Error writing record."}""", "Server information: Error writing record (HTTP 502).")]
    [InlineData("""{"rc":8,"category":6,"reason":3,"message":"..."}""", "Server information: server error (reason 3).")]
    public void A_body_that_is_not_an_mvsmf_error_still_gives_a_message(string body, string expected) =>
        Assert.Equal(expected, MvsmfErrors.FromResponse(HttpStatusCode.BadGateway, Encoding.UTF8.GetBytes(body), "Server information").Message);

    [Fact]
    public void Authorization_is_500_is_reported_as_not_authorized()
    {
        var body = Encoding.UTF8.GetBytes("""{"rc":8,"category":4,"reason":0,"message":"LMOPEN error"}""");
        var ex = MvsmfErrors.FromResponse(HttpStatusCode.InternalServerError, body, "SYS1.SECRET(X)");
        Assert.Equal(HostFileErrorKind.NotAuthorized, ex.Kind);
        Assert.Equal("SYS1.SECRET(X): not authorized.", ex.Message);
    }

    [Fact]
    public void A_401_says_the_credentials_were_rejected() =>
        Assert.Equal("The host rejected the userid or password.",
            MvsmfErrors.FromResponse(HttpStatusCode.Unauthorized, [], "anything").Message);

    [Fact]
    public async Task Create_failure_is_one_500_so_it_names_every_cause()
    {
        using var response = Fixture.Load("create-dynalloc-500");
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        var ex = MvsmfErrors.FromResponse(response.StatusCode, body, "IBMUSER.LIZITEST.FIX");

        Assert.Equal(HostFileErrorKind.CannotAllocate, ex.Kind);
        Assert.Equal(7, ex.Reason);
        Assert.Equal("Dynamic allocation Error", ex.ServerMessage);
        Assert.Equal("IBMUSER.LIZITEST.FIX: the host could not allocate it (it may already exist, there may be no space, or you may not be authorized).", ex.Message);
    }

    [Fact]
    public async Task Rename_target_exists_400_is_already_exists()
    {
        using var response = Fixture.Load("rename-member-exists");
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        var ex = MvsmfErrors.FromResponse(response.StatusCode, body, "Rename A.B(ONE) to TWO");

        Assert.Equal(HostFileErrorKind.AlreadyExists, ex.Kind);
        Assert.Equal(7, ex.Reason);
        Assert.Equal("Rename A.B(ONE) to TWO: a member of that name already exists.", ex.Message);
    }

    [Fact]
    public async Task A_stale_if_match_is_a_conflict()
    {
        using var response = Fixture.Load("write-412");
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        var ex = MvsmfErrors.FromResponse(response.StatusCode, body, "A.B(ONE)");

        Assert.Equal(HostFileErrorKind.Conflict, ex.Kind);
        Assert.Equal(10, ex.Reason);
        Assert.Equal("A.B(ONE): changed on the host since it was read.", ex.Message);
    }

    [Fact]
    public async Task A_dataset_renamed_onto_an_existing_name_is_the_hosts_500()
    {
        // No fixture: IDCAMS ALTER refuses it and the host answers reason 8 like any other rename failure.
        var body = "{\"rc\":8,\"category\":6,\"reason\":8,\"message\":\"Rename operation failed\"}"u8.ToArray();

        var ex = MvsmfErrors.FromResponse(HttpStatusCode.InternalServerError, body, "Rename A.B to A.C");

        Assert.Equal(HostFileErrorKind.ServerError, ex.Kind);
        Assert.Equal("Rename A.B to A.C: Rename operation failed (reason 8).", ex.Message);
    }
}
