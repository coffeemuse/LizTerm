// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfReadTests
{
    private static readonly HostPath Jes2 = HostPath.ForMember("SYS1.PROCLIB", "JES2");

    private static MvsmfFileService Service(RecordedHandler handler, TimeSpan? idle = null) =>
        new(handler, MvsmfAuthTests.Base, MvsmfAuthTests.Answering([], new HostCredentials("MVSCE02", "pw")), idle);

    private static RecordedHandler Answering(byte[] body) => new RecordedHandler().Then((_, _) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }));

    private static RecordedHandler Answering(Stream body) => new RecordedHandler().Then((_, _) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }));

    [Fact]
    public async Task A_text_read_returns_one_line_per_record()
    {
        var handler = new RecordedHandler().Then("read-text-jes2");
        using var service = Service(handler);

        var lines = await service.ReadTextAsync(Jes2, cancellationToken: TestContext.Current.CancellationToken);

        // The recording has five spaces before PROC.
        Assert.StartsWith("//JES2     PROC M=JES2PM00,", lines[0]);
        Assert.Equal(80, lines[0].Length);
        Assert.EndsWith("00000010", lines[0]);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("text", request.DataType);
        Assert.Equal("/zosmf/restfiles/ds/SYS1.PROCLIB(JES2)", request.Uri.PathAndQuery);
    }

    [Fact]
    public async Task Text_body_is_latin1()
    {
        using var service = Service(Answering([0x41, 0xAC, 0xA2, 0x42, 0x0A]));
        Assert.Equal(new[] { "A¬¢B" }, await service.ReadTextAsync(Jes2, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Text_read_keeps_trailing_blanks_and_blank_records()
    {
        using var service = Service(Answering("AB  \n\nC\n"u8.ToArray()));
        Assert.Equal(new[] { "AB  ", "", "C" }, await service.ReadTextAsync(Jes2, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("A", new[] { "A" })]
    [InlineData("A\n", new[] { "A" })]
    [InlineData("A\r\n", new[] { "A\r" })]
    public void SplitRecords_splits_at_lf_only(string body, string[] expected) =>
        Assert.Equal(expected, MvsmfFileService.SplitRecords(System.Text.Encoding.Latin1.GetBytes(body)));

    [Fact]
    public async Task A_binary_read_copies_every_byte_and_reports_progress()
    {
        var handler = new RecordedHandler().Then("read-binary-jes2");
        using var service = Service(handler);
        using var destination = new MemoryStream();
        var progress = new ListProgress();

        var count = await service.ReadBinaryAsync(Jes2, destination, progress, TestContext.Current.CancellationToken);

        var expected = Fixture.Body("read-binary-jes2");
        Assert.Equal(expected, destination.ToArray());
        Assert.Equal(expected.Length, count);
        Assert.Equal(expected.Length, progress.Values[^1]);
        Assert.Equal("binary", handler.Requests[0].DataType);
    }

    [Fact]
    public async Task A_missing_member_is_not_found()
    {
        using var service = Service(new RecordedHandler().Then("read-missing-member"));
        var ex = await Assert.ThrowsAsync<HostFileException>(() =>
            service.ReadTextAsync(HostPath.ForMember("SYS1.PROCLIB", "NOSUCHMB"), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);
    }

    [Fact(Timeout = 30000)]
    public async Task A_host_that_stops_sending_times_out()
    {
        using var service = Service(Answering(new StallingStream()), idle: TimeSpan.FromMilliseconds(200));

        var ex = await Assert.ThrowsAsync<HostFileException>(() =>
            service.ReadBinaryAsync(Jes2, new MemoryStream(), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unreachable, ex.Kind);
        Assert.StartsWith("SYS1.PROCLIB(JES2): the host stopped answering", ex.Message);
    }

    [Fact(Timeout = 30000)]
    public async Task Cancelling_is_not_reported_as_a_timeout()
    {
        using var service = Service(Answering(new StallingStream()));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadBinaryAsync(Jes2, new MemoryStream(), cancellationToken: cancel.Token));
    }

    [Fact]
    public async Task A_dropped_connection_is_unreachable()
    {
        using var service = Service(Answering(new FailingStream()));

        var ex = await Assert.ThrowsAsync<HostFileException>(() =>
            service.ReadTextAsync(Jes2, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.Unreachable, ex.Kind);
        Assert.Equal("SYS1.PROCLIB(JES2): the connection dropped during the transfer.", ex.Message);
    }
}
