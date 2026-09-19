// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

/// <summary>Pins the <c>etag</c> compatibility entry: the stamp is asked for on every read and write, echoed back
/// verbatim as If-Match, and a 412 is a conflict.</summary>
public class MvsmfEtagTests
{
    private static readonly HostPath One = HostPath.ForMember("IBMUSER.LIZITEST.FIX", "ONE");

    private static MvsmfFileService Service(RecordedHandler handler) =>
        new(handler, MvsmfAuthTests.Base, MvsmfAuthTests.Providing([], new HostCredentials("MVSCE02", "pw")));

    private static string RecordedEtag(string fixture)
    {
        using var response = Fixture.Load(fixture);
        return response.Headers.GetValues("ETag").Single();
    }

    [Fact]
    public async Task Etag_is_asked_for_on_a_text_read_and_returned()
    {
        var handler = new RecordedHandler().Then("login-200").Then("read-etag");
        using var service = Service(handler);

        var read = await service.ReadTextAsync(One, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("true", handler.Requests[1].Headers["X-IBM-Return-Etag"]);
        Assert.Equal(RecordedEtag("read-etag"), read.Etag);
        Assert.Matches("^[0-9A-Fa-f]{16}$", read.Etag!);
        Assert.StartsWith("//ONE JOB", read.Lines[0]);
    }

    [Fact]
    public async Task Etag_is_asked_for_on_a_binary_read_and_returned()
    {
        var handler = new RecordedHandler().Then("login-200").Then("read-etag");
        using var service = Service(handler);

        var read = await service.ReadBinaryAsync(One, new MemoryStream(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("true", handler.Requests[1].Headers["X-IBM-Return-Etag"]);
        Assert.Equal(RecordedEtag("read-etag"), read.Etag);
    }

    [Fact]
    public async Task A_read_without_a_stamp_returns_null()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("read-text-jes2"));

        var read = await service.ReadTextAsync(HostPath.ForMember("SYS1.PROCLIB", "JES2"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(read.Etag);
    }

    [Fact]
    public async Task Etag_is_asked_for_on_a_write_and_the_new_stamp_returned()
    {
        var handler = new RecordedHandler().Then("login-200").Then("write-etag-204").Then("write-etag-204");
        using var service = Service(handler);

        var text = await service.WriteTextAsync(One, ["//ONE JOB"], cancellationToken: TestContext.Current.CancellationToken);
        var binary = await service.WriteBinaryAsync(One, new MemoryStream([1]), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(RecordedEtag("write-etag-204"), text);
        Assert.Equal(RecordedEtag("write-etag-204"), binary);
        Assert.Equal("true", handler.Requests[1].Headers["X-IBM-Return-Etag"]);
        Assert.DoesNotContain("If-Match", handler.Requests[1].HeaderNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task If_match_is_sent_as_given()
    {
        var handler = new RecordedHandler().Then("login-200").Then("write-etag-204");
        using var service = Service(handler);

        await service.WriteTextAsync(One, ["A"], ifMatch: "7F3A00000000BEEF", TestContext.Current.CancellationToken);

        Assert.Equal("7F3A00000000BEEF", handler.Requests[1].Headers["If-Match"]);
    }

    [Fact]
    public async Task A_stale_if_match_is_a_conflict_and_the_write_is_not_repeated()
    {
        var handler = new RecordedHandler().Then("login-200").Then("write-412");
        using var service = Service(handler);

        var ex = await Assert.ThrowsAsync<HostFileException>(() =>
            service.WriteTextAsync(One, ["A"], ifMatch: "0000000000000000", TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Conflict, ex.Kind);
        Assert.Equal("IBMUSER.LIZITEST.FIX(ONE): changed on the host since it was read.", ex.Message);
        Assert.Single(handler.Requests, r => r.Method == HttpMethod.Put);
    }

    [Theory]
    [InlineData("7F3A", "7F3A")]
    [InlineData("\"7F3A\"", "7F3A")]
    [InlineData("W/\"7F3A\"", "7F3A")]
    [InlineData("  7F3A ", "7F3A")]
    [InlineData("", null)]
    [InlineData("\"\"", null)]
    public void The_stamp_is_taken_from_the_header_as_bare_text(string header, string? expected)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("ETag", header);

        Assert.Equal(expected, MvsmfFileService.EtagOf(response));
    }

    [Fact]
    public void No_header_is_no_stamp()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.Null(MvsmfFileService.EtagOf(response));
    }
}
