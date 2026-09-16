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
    [InlineData(HttpStatusCode.InternalServerError, 6, 8, 3, HostFileErrorKind.CannotOpen)]
    [InlineData(HttpStatusCode.NotFound, 6, 8, 5, HostFileErrorKind.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, 6, 8, 4, HostFileErrorKind.NotFound)]
    [InlineData(HttpStatusCode.NotFound, 4, 6, 7, HostFileErrorKind.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, 4, 8, 0, HostFileErrorKind.NotAuthorized)]
    [InlineData(HttpStatusCode.Forbidden, null, null, null, HostFileErrorKind.NotAuthorized)]
    [InlineData(HttpStatusCode.BadRequest, 6, 8, 1, HostFileErrorKind.InvalidRequest)]
    [InlineData(HttpStatusCode.InternalServerError, 8, 900, 7, HostFileErrorKind.ServerError)]
    [InlineData(HttpStatusCode.BadGateway, null, null, null, HostFileErrorKind.ServerError)]
    public void Classifies_by_reason_before_status(HttpStatusCode status, int? category, int? rc, int? reason, HostFileErrorKind expected) =>
        Assert.Equal(expected, MvsmfErrors.Classify(status, category, rc, reason));

    [Fact]
    public async Task Missing_read_is_500_is_reported_as_cannot_open()
    {
        using var response = Fixture.Load("read-missing-member");
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        var ex = MvsmfErrors.FromResponse(response.StatusCode, body, "SYS1.PROCLIB(NOSUCHMB)");

        Assert.Equal(HostFileErrorKind.CannotOpen, ex.Kind);
        Assert.Equal(3, ex.Reason);
        Assert.Equal("Cannot open dataset member", ex.ServerMessage);
        Assert.Equal("SYS1.PROCLIB(NOSUCHMB): not found, not authorized, or cannot be opened.", ex.Message);
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

    [Theory]
    [InlineData("", "Server information: server error (HTTP 502).")]
    [InlineData("<html>gateway</html>", "Server information: server error (HTTP 502).")]
    [InlineData("""{"rc":8,"category":9,"reason":12,"message":"odd"}""", "Server information: server error (reason 12).")]
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
}
