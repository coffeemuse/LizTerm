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
        new(handler, MvsmfAuthTests.Base, MvsmfAuthTests.Answering([], new HostCredentials("MVSCE02", "pw")));

    [Fact]
    public async Task A_text_write_puts_latin1_records_ending_in_lf()
    {
        var handler = new RecordedHandler().Then("write-204");
        using var service = Service(handler);

        await service.WriteTextAsync(NewMember, ["//A JOB", "¬¢"], TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/zosmf/restfiles/ds/MVSCE02.CNTL(NEWMEM)", request.Uri.PathAndQuery);
        Assert.Equal("text", request.DataType);
        Assert.Equal("text/plain", request.ContentType);
        Assert.Equal(Encoding.Latin1.GetBytes("//A JOB\n¬¢\n"), request.Body);
    }

    [Fact]
    public async Task An_empty_line_is_sent_as_an_empty_record()
    {
        var handler = new RecordedHandler().Then("write-204");
        using var service = Service(handler);

        await service.WriteTextAsync(NewMember, ["A", "", "B"], TestContext.Current.CancellationToken);

        Assert.Equal("A\n\nB\n", handler.Requests[0].BodyText);
    }

    [Fact]
    public async Task Put_json_is_rename_so_no_write_ever_sends_json()
    {
        var handler = new RecordedHandler().Then("write-204").Then("write-204");
        using var service = Service(handler);

        await service.WriteTextAsync(NewMember, ["A"], TestContext.Current.CancellationToken);
        await service.WriteBinaryAsync(NewMember, new MemoryStream([1, 2]), TestContext.Current.CancellationToken);

        Assert.All(handler.Requests, r => Assert.DoesNotContain("json", r.ContentType ?? "", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task No_lines_send_an_empty_body()
    {
        var handler = new RecordedHandler().Then("write-204");
        using var service = Service(handler);

        await service.WriteTextAsync(NewMember, [], TestContext.Current.CancellationToken);

        Assert.Empty(handler.Requests[0].Body!);
    }

    [Theory]
    [InlineData("price 5€")]
    [InlineData("two\nlines")]
    [InlineData("two\rlines")]
    public async Task A_line_the_host_cannot_store_is_refused_before_sending(string line)
    {
        var handler = new RecordedHandler();
        using var service = Service(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => service.WriteTextAsync(NewMember, [line], TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_binary_write_puts_the_bytes_as_an_octet_stream()
    {
        var handler = new RecordedHandler().Then("write-204");
        using var service = Service(handler);

        await service.WriteBinaryAsync(NewMember, new MemoryStream([0x61, 0x61, 0x00, 0xFF]), TestContext.Current.CancellationToken);

        var request = handler.Requests[0];
        Assert.Equal("binary", request.DataType);
        Assert.Equal("application/octet-stream", request.ContentType);
        Assert.Equal(new byte[] { 0x61, 0x61, 0x00, 0xFF }, request.Body);
    }

    [Fact]
    public async Task The_repeat_after_a_401_sends_the_same_body()
    {
        var handler = new RecordedHandler().Then(HttpStatusCode.Unauthorized).Then("write-204")
            .Then(HttpStatusCode.Unauthorized).Then("write-204");
        using var service = Service(handler);

        await service.WriteTextAsync(NewMember, ["A", "B"], TestContext.Current.CancellationToken);
        await service.WriteBinaryAsync(NewMember, new MemoryStream([9, 8, 7]), TestContext.Current.CancellationToken);

        Assert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);
        Assert.Equal(handler.Requests[2].Body, handler.Requests[3].Body);
        Assert.Equal(new byte[] { 9, 8, 7 }, handler.Requests[3].Body);
    }

    [Fact]
    public async Task A_member_is_deleted()
    {
        var handler = new RecordedHandler().Then("delete-204");
        using var service = Service(handler);

        await service.DeleteAsync(NewMember, TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/zosmf/restfiles/ds/MVSCE02.CNTL(NEWMEM)", request.Uri.PathAndQuery);
    }

    [Fact]
    public async Task Deleting_a_missing_member_is_not_found()
    {
        using var service = Service(new RecordedHandler().Then("delete-missing"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.DeleteAsync(NewMember, TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);
        Assert.Equal("MVSCE02.CNTL(NEWMEM): not found.", ex.Message);
    }

    [Fact]
    public async Task A_whole_dataset_is_never_deleted()
    {
        var handler = new RecordedHandler();
        using var service = Service(handler);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            service.DeleteAsync(HostPath.ForDataset("MVSCE02.CNTL"), TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }
}
