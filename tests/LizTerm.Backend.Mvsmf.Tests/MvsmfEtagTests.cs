// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

/// <summary>Pins the <c>etag</c> compatibility entry: the stamp is asked for on a read that wants it and on every
/// write, kept as the host sent it, echoed back verbatim as If-Match, and a 412 is a conflict.</summary>
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

        var read = await service.ReadTextAsync(One, withEtag: true, cancellationToken: TestContext.Current.CancellationToken);

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

        var read = await service.ReadBinaryAsync(One, new MemoryStream(), withEtag: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("true", handler.Requests[1].Headers["X-IBM-Return-Etag"]);
        Assert.Equal(RecordedEtag("read-etag"), read.Etag);
    }

    [Fact]
    public async Task A_read_that_does_not_ask_sends_no_header_and_has_no_stamp()
    {
        // The stamp costs the host a second pass over the member, so a read that will not be written back must not
        // ask for it: the verify read-back after an upload, for one. The recorded answer carries an ETag anyway
        // (it was recorded with the header); an unasked read still reports none.
        var handler = new RecordedHandler().Then("login-200").Then("read-etag").Then("read-etag");
        using var service = Service(handler);

        var text = await service.ReadTextAsync(One, cancellationToken: TestContext.Current.CancellationToken);
        var binary = await service.ReadBinaryAsync(One, new MemoryStream(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("X-IBM-Return-Etag", handler.Requests[1].HeaderNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("X-IBM-Return-Etag", handler.Requests[2].HeaderNames, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("text", handler.Requests[1].DataType);
        Assert.Equal("binary", handler.Requests[2].DataType);
        Assert.Null(text.Etag);
        Assert.Null(binary.Etag);
    }

    [Fact]
    public async Task A_read_that_asks_of_a_host_that_gives_no_stamp_returns_null()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("read-text-jes2"));

        var read = await service.ReadTextAsync(HostPath.ForMember("SYS1.PROCLIB", "JES2"), withEtag: true, cancellationToken: TestContext.Current.CancellationToken);

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
        Assert.Equal("true", handler.Requests[2].Headers["X-IBM-Return-Etag"]);
        Assert.DoesNotContain("If-Match", handler.Requests[1].HeaderNames, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("7F3A00000000BEEF")]
    [InlineData("\"7F3A00000000BEEF\"")]
    [InlineData("W/\"7F3A00000000BEEF\"")]
    public async Task If_match_is_sent_as_given_quoted_or_not(string stamp)
    {
        var handler = new RecordedHandler().Then("login-200").Then("write-etag-204");
        using var service = Service(handler);

        await service.WriteTextAsync(One, ["A"], ifMatch: stamp, TestContext.Current.CancellationToken);

        Assert.Equal(stamp, handler.Requests[1].Headers["If-Match"]);
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

    /// <summary>A stamp is opaque: what the host sent is what goes back, so a host that quotes its entity tags
    /// (RFC 7232) matches its own text, and mvsMF's bare hex goes back bare.</summary>
    [Theory]
    [InlineData("7F3A", "7F3A")]
    [InlineData("\"7F3A\"", "\"7F3A\"")]
    [InlineData("W/\"7F3A\"", "W/\"7F3A\"")]
    [InlineData("  7F3A ", "7F3A")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void The_stamp_is_taken_from_the_header_as_the_host_sent_it(string header, string? expected)
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
