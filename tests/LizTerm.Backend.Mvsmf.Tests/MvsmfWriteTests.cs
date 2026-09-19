// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Text;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfWriteTests
{
    private static readonly HostPath NewMember = HostPath.ForMember("MVSCE02.CNTL", "NEWMEM");

    private static MvsmfFileService Service(RecordedHandler handler) =>
        new(handler, MvsmfAuthTests.Base, MvsmfAuthTests.Providing([], new HostCredentials("MVSCE02", "pw")));

    [Fact]
    public async Task A_text_write_puts_latin1_records_ending_in_lf()
    {
        var handler = new RecordedHandler().Then("login-200").Then("write-204");
        using var service = Service(handler);

        await service.WriteTextAsync(NewMember, ["//A JOB", "¬¢"], cancellationToken: TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Put);
        Assert.Equal("/zosmf/restfiles/ds/MVSCE02.CNTL(NEWMEM)", request.Uri.PathAndQuery);
        Assert.Equal("text", request.DataType);
        Assert.Equal("text/plain", request.ContentType);
        Assert.Equal(Encoding.Latin1.GetBytes("//A JOB\n¬¢\n"), request.Body);
    }

    [Fact]
    public async Task An_empty_line_is_sent_as_an_empty_record()
    {
        var handler = new RecordedHandler().Then("login-200").Then("write-204");
        using var service = Service(handler);

        await service.WriteTextAsync(NewMember, ["A", "", "B"], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("A\n\nB\n", handler.Requests[1].BodyText);
    }

    [Fact]
    public async Task Put_json_is_rename_so_no_write_ever_sends_json()
    {
        var handler = new RecordedHandler().Then("login-200").Then("write-204").Then("write-204");
        using var service = Service(handler);

        await service.WriteTextAsync(NewMember, ["A"], cancellationToken: TestContext.Current.CancellationToken);
        await service.WriteBinaryAsync(NewMember, new MemoryStream([1, 2]), cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(handler.Requests, r => Assert.DoesNotContain("json", r.ContentType ?? "", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task No_lines_send_an_empty_body()
    {
        var handler = new RecordedHandler().Then("login-200").Then("write-204");
        using var service = Service(handler);

        await service.WriteTextAsync(NewMember, [], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(handler.Requests[1].Body!);
    }

    [Theory]
    [InlineData("price 5€")]
    [InlineData("two\nlines")]
    [InlineData("two\rlines")]
    public async Task A_line_the_host_cannot_store_is_refused_before_sending(string line)
    {
        var handler = new RecordedHandler();
        using var service = Service(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => service.WriteTextAsync(NewMember, [line], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_binary_write_puts_the_bytes_as_an_octet_stream()
    {
        var handler = new RecordedHandler().Then("login-200").Then("write-204");
        using var service = Service(handler);

        await service.WriteBinaryAsync(NewMember, new MemoryStream([0x61, 0x61, 0x00, 0xFF]), cancellationToken: TestContext.Current.CancellationToken);

        var request = handler.Requests[1];
        Assert.Equal("binary", request.DataType);
        Assert.Equal("application/octet-stream", request.ContentType);
        Assert.Equal(new byte[] { 0x61, 0x61, 0x00, 0xFF }, request.Body);
    }

    [Fact]
    public async Task The_repeat_after_a_401_sends_the_same_body()
    {
        // Each 401 sends the provider back for a fresh token, so a login sits before every repeated PUT.
        var handler = new RecordedHandler()
            .Then("login-200").Then(HttpStatusCode.Unauthorized).Then("login-200").Then("write-204")
            .Then(HttpStatusCode.Unauthorized).Then("login-200").Then("write-204");
        using var service = Service(handler);

        await service.WriteTextAsync(NewMember, ["A", "B"], cancellationToken: TestContext.Current.CancellationToken);
        await service.WriteBinaryAsync(NewMember, new MemoryStream([9, 8, 7]), cancellationToken: TestContext.Current.CancellationToken);

        var puts = handler.Requests.Where(r => r.Method == HttpMethod.Put).ToList();
        Assert.Equal(4, puts.Count);
        Assert.Equal(puts[0].Body, puts[1].Body);
        Assert.Equal(puts[2].Body, puts[3].Body);
        Assert.Equal(new byte[] { 9, 8, 7 }, puts[3].Body);
    }
}
