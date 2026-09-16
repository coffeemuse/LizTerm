// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;
using LizTerm.Core.HostFiles;

namespace LizTerm.Core.Tests.HostFiles;

public sealed class HostFileTransferTests : IDisposable
{
    private static readonly HostPath Jes2 = HostPath.ForMember("SYS1.PROCLIB", "JES2");
    private static readonly DatasetAttributes Fb80 = new("PO", "FB", 80, 19040, null);
    private readonly string _directory = Directory.CreateTempSubdirectory("lizterm-hostfiles-").FullName;
    private readonly FakeHostFileService _host = new();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Local(string name) => Path.Combine(_directory, name);

    [Fact]
    public async Task A_text_download_trims_trailing_blanks_and_uses_the_line_ending_asked_for()
    {
        _host.Text[Jes2.ToString()] = ["//JES2    PROC   ", "", "//  END  "];

        var written = await HostFileTransfer.DownloadAsync(_host, Jes2, Local("jes2.txt"),
            new DownloadOptions(HostTransferMode.Text, LineEnding: "\n"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("//JES2    PROC\n\n//  END\n", await File.ReadAllTextAsync(Local("jes2.txt"), TestContext.Current.CancellationToken));
        Assert.Equal(24, written);
    }

    [Fact]
    public async Task Trailing_blanks_can_be_kept_and_the_file_is_utf8_without_a_mark()
    {
        _host.Text[Jes2.ToString()] = ["¬ ¢  "];

        await HostFileTransfer.DownloadAsync(_host, Jes2, Local("keep.txt"),
            new DownloadOptions(HostTransferMode.Text, TrimTrailingBlanks: false, LineEnding: "\r\n"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Encoding.UTF8.GetBytes("¬ ¢  \r\n"), await File.ReadAllBytesAsync(Local("keep.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void The_default_line_ending_is_the_platform_one() =>
        Assert.Equal("A" + Environment.NewLine, HostFileTransfer.FormatText(["A"], new DownloadOptions(HostTransferMode.Text)));

    [Fact]
    public async Task A_binary_download_copies_the_bytes()
    {
        _host.Binary[Jes2.ToString()] = [0x61, 0x61, 0xD1];

        var written = await HostFileTransfer.DownloadAsync(_host, Jes2, Local("jes2.bin"),
            new DownloadOptions(HostTransferMode.Binary), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, written);
        Assert.Equal(new byte[] { 0x61, 0x61, 0xD1 }, await File.ReadAllBytesAsync(Local("jes2.bin"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_failed_download_leaves_the_existing_file_and_no_partial_file()
    {
        await File.WriteAllTextAsync(Local("keep.bin"), "old", TestContext.Current.CancellationToken);
        _host.ReadFailure = new HostFileException(HostFileErrorKind.Unreachable, "dropped");
        _host.BytesBeforeFailure = 100;

        await Assert.ThrowsAsync<HostFileException>(() => HostFileTransfer.DownloadAsync(_host, Jes2, Local("keep.bin"),
            new DownloadOptions(HostTransferMode.Binary), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("old", await File.ReadAllTextAsync(Local("keep.bin"), TestContext.Current.CancellationToken));
        Assert.Equal(new[] { Local("keep.bin") }, Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task A_cancelled_download_leaves_no_file()
    {
        _host.Text[Jes2.ToString()] = ["A"];
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HostFileTransfer.DownloadAsync(_host, Jes2, Local("gone.txt"),
            new DownloadOptions(HostTransferMode.Text), cancellationToken: cancelled.Token));

        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task A_verified_upload_matches_when_the_host_pads_records()
    {
        _host.StoreTransform = lines => [.. lines.Select(l => l.PadRight(80))];
        var checkedText = HostFileTransfer.CheckTextFile(await WriteLocal("up.jcl", "//A JOB\n\n//B\n"), Fb80);

        var outcome = await HostFileTransfer.UploadTextAsync(_host, Jes2, checkedText, verify: true, TestContext.Current.CancellationToken);

        Assert.Equal(UploadOutcome.Matches, outcome);
        Assert.Equal(new[] { "writetext:SYS1.PROCLIB(JES2):3", "readtext:SYS1.PROCLIB(JES2)" }, _host.Calls);
    }

    [Fact]
    public async Task A_verified_upload_reports_the_first_line_that_differs()
    {
        _host.StoreTransform = lines => [.. lines.Where(l => l.Length > 0)];
        var checkedText = HostFileTransfer.CheckTextFile(await WriteLocal("up.jcl", "//A JOB\n\n//B\n"), Fb80);

        var outcome = await HostFileTransfer.UploadTextAsync(_host, Jes2, checkedText, verify: true, TestContext.Current.CancellationToken);

        Assert.Equal(new UploadOutcome(UploadVerification.Differs, 2), outcome);
    }

    [Fact]
    public async Task An_unverified_upload_does_not_read_back()
    {
        var checkedText = HostFileTransfer.CheckTextFile(await WriteLocal("up.jcl", "//A JOB\n"), Fb80);

        var outcome = await HostFileTransfer.UploadTextAsync(_host, Jes2, checkedText, verify: false, TestContext.Current.CancellationToken);

        Assert.Equal(UploadOutcome.NotChecked, outcome);
        Assert.Equal(new[] { "writetext:SYS1.PROCLIB(JES2):1" }, _host.Calls);
    }

    [Fact]
    public async Task Text_that_failed_its_check_is_never_sent()
    {
        var checkedText = HostFileTransfer.CheckTextFile(await WriteLocal("long.txt", new string('X', 81)), Fb80);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            HostFileTransfer.UploadTextAsync(_host, Jes2, checkedText, verify: true, TestContext.Current.CancellationToken));
        Assert.Empty(_host.Calls);
    }

    [Fact]
    public async Task A_binary_upload_sends_the_file_bytes()
    {
        var source = Local("load.bin");
        await File.WriteAllBytesAsync(source, [1, 2, 3], TestContext.Current.CancellationToken);

        await HostFileTransfer.UploadBinaryAsync(_host, Jes2, source, TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 1, 2, 3 }, _host.Binary[Jes2.ToString()]);
    }

    [Theory]
    [InlineData(new[] { "A", "B" }, new[] { "A  ", "B" }, null)]
    [InlineData(new[] { "A", "B" }, new[] { "A", "C" }, 2)]
    [InlineData(new[] { "A", "B" }, new[] { "A" }, 2)]
    [InlineData(new[] { "A" }, new[] { "A", "B" }, 2)]
    [InlineData(new string[0], new string[0], null)]
    public void FirstDifference_trims_trailing_blanks_and_counts_missing_lines(string[] sent, string[] back, int? expected) =>
        Assert.Equal(expected, HostFileTransfer.FirstDifference(sent, back));

    private async Task<string> WriteLocal(string name, string text)
    {
        await File.WriteAllTextAsync(Local(name), text, TestContext.Current.CancellationToken);
        return Local(name);
    }
}
