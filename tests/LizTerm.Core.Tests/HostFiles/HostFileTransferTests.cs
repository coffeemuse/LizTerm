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
        Assert.Equal(24, written.BytesWritten);
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
    public void Format_trims_trailing_blanks_when_the_option_is_on() =>
        Assert.Equal("//HELLO JOB", new DownloadOptions(HostTransferMode.Text).Format("//HELLO JOB   "));

    [Fact]
    public void Format_keeps_them_when_it_is_off() =>
        Assert.Equal("//HELLO JOB   ",
            new DownloadOptions(HostTransferMode.Text, TrimTrailingBlanks: false).Format("//HELLO JOB   "));

    [Fact]
    public void Format_leaves_leading_blanks_and_tabs_alone() =>
        Assert.Equal("\t  indented\t", new DownloadOptions(HostTransferMode.Text).Format("\t  indented\t  "));

    [Fact]
    public void The_default_line_ending_is_the_platform_one() =>
        Assert.Equal("A" + Environment.NewLine, HostFileTransfer.FormatText(["A"], new DownloadOptions(HostTransferMode.Text)));

    [Fact]
    public async Task A_download_to_the_longest_legal_file_name_works()
    {
        _host.Binary[Jes2.ToString()] = [0x61];
        var name = new string('j', 250) + ".bin";

        await HostFileTransfer.DownloadAsync(_host, Jes2, Local(name),
            new DownloadOptions(HostTransferMode.Binary), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 0x61 }, await File.ReadAllBytesAsync(Local(name), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_binary_download_copies_the_bytes()
    {
        _host.Binary[Jes2.ToString()] = [0x61, 0x61, 0xD1];

        var written = await HostFileTransfer.DownloadAsync(_host, Jes2, Local("jes2.bin"),
            new DownloadOptions(HostTransferMode.Binary), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, written.BytesWritten);
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

        var outcome = await HostFileTransfer.UploadTextAsync(_host, Jes2, checkedText, verify: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(UploadVerification.Matches, outcome.Verification);
        Assert.Equal(new[] { "writetext:SYS1.PROCLIB(JES2):3", "readtext:SYS1.PROCLIB(JES2)" }, _host.Calls);
    }

    [Fact]
    public async Task A_verified_upload_reports_the_first_line_that_differs()
    {
        _host.StoreTransform = lines => [.. lines.Where(l => l.Length > 0)];
        var checkedText = HostFileTransfer.CheckTextFile(await WriteLocal("up.jcl", "//A JOB\n\n//B\n"), Fb80);

        var outcome = await HostFileTransfer.UploadTextAsync(_host, Jes2, checkedText, verify: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal((UploadVerification.Differs, 2), (outcome.Verification, outcome.DiffersAtLine));
    }

    [Fact]
    public async Task An_unverified_upload_does_not_read_back()
    {
        var checkedText = HostFileTransfer.CheckTextFile(await WriteLocal("up.jcl", "//A JOB\n"), Fb80);

        var outcome = await HostFileTransfer.UploadTextAsync(_host, Jes2, checkedText, verify: false, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(UploadVerification.NotChecked, outcome.Verification);
        Assert.Equal(new[] { "writetext:SYS1.PROCLIB(JES2):1" }, _host.Calls);
    }

    [Fact]
    public async Task Text_that_failed_its_check_is_never_sent()
    {
        var checkedText = HostFileTransfer.CheckTextFile(await WriteLocal("long.txt", new string('X', 81)), Fb80);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            HostFileTransfer.UploadTextAsync(_host, Jes2, checkedText, verify: true, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(_host.Calls);
    }

    [Fact]
    public async Task A_binary_upload_sends_the_file_bytes()
    {
        var source = Local("load.bin");
        await File.WriteAllBytesAsync(source, [1, 2, 3], TestContext.Current.CancellationToken);

        await HostFileTransfer.UploadBinaryAsync(_host, Jes2, source, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 1, 2, 3 }, _host.Binary[Jes2.ToString()]);
    }

    [Fact]
    public async Task A_download_returns_the_stamp_the_host_gave()
    {
        _host.Text[Jes2.ToString()] = ["A"];
        _host.Etags[Jes2.ToString()] = "stamp-7";

        var result = await HostFileTransfer.DownloadAsync(_host, Jes2, Local("a.txt"),
            new DownloadOptions(HostTransferMode.Text), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("stamp-7", result.Etag);
        Assert.Equal(new[] { "readtext:SYS1.PROCLIB(JES2):etag" }, _host.Calls);
    }

    [Fact]
    public async Task A_download_of_an_unstamped_member_returns_no_stamp()
    {
        _host.Binary[Jes2.ToString()] = [1];

        var result = await HostFileTransfer.DownloadAsync(_host, Jes2, Local("a.bin"),
            new DownloadOptions(HostTransferMode.Binary), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result.Etag);
        Assert.Equal(new[] { "readbinary:SYS1.PROCLIB(JES2):etag" }, _host.Calls);
    }

    [Fact]
    public async Task An_upload_passes_the_stamp_on_and_returns_the_new_one()
    {
        var checkedText = HostFileTransfer.CheckTextFile(await WriteLocal("up.jcl", "//A JOB\n"), Fb80);

        var outcome = await HostFileTransfer.UploadTextAsync(_host, Jes2, checkedText, verify: false, ifMatch: "stamp-1", TestContext.Current.CancellationToken);
        var binary = await HostFileTransfer.UploadBinaryAsync(_host, Jes2, await WriteLocal("up.bin", "x"), ifMatch: "stamp-2", TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "stamp-1", "stamp-2" }, _host.IfMatches);
        Assert.Equal("write-1", outcome.Etag);
        Assert.Equal("write-2", binary);
    }

    [Fact]
    public async Task A_verified_upload_keeps_the_write_stamp_and_reads_back_without_asking_for_one()
    {
        // The read-back's stamp would be discarded, and asking for it costs the host another pass over the member.
        var checkedText = HostFileTransfer.CheckTextFile(await WriteLocal("up.jcl", "//A JOB\n"), Fb80);

        var outcome = await HostFileTransfer.UploadTextAsync(_host, Jes2, checkedText, verify: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("write-1", outcome.Etag);
        Assert.Equal(new[] { "writetext:SYS1.PROCLIB(JES2):1", "readtext:SYS1.PROCLIB(JES2)" }, _host.Calls);
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

    [Fact]
    public void A_binary_upload_over_the_cap_is_named_before_any_request()
    {
        var file = Path.Combine(Path.GetTempPath(), $"liz-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(file, new byte[10]);
            Assert.Null(HostFileTransfer.BinaryUploadProblem(file, 10));
            Assert.Equal("The file is 10 bytes; the host holds at most 9.", HostFileTransfer.BinaryUploadProblem(file, 9));
            File.WriteAllBytes(file, new byte[HostFileLimits.MaxUnixFileBytes + 1]);
            Assert.Equal("The file is 65,537 bytes; the host holds at most 65,536.", HostFileTransfer.BinaryUploadProblem(file, HostFileLimits.MaxUnixFileBytes));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void A_unix_text_file_is_checked_without_a_record_length()
    {
        var file = Path.Combine(Path.GetTempPath(), $"liz-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(file, new string('x', 200) + "\n");
            Assert.True(HostFileTransfer.CheckUnixTextFile(file).CanUpload);
            Assert.False(HostFileTransfer.CheckUnixTextFile(file, maxBytes: 100).CanUpload);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void A_listing_cut_short_without_a_continuation_is_not_complete()
    {
        Assert.True(new HostFileListing([], null).IsComplete);
        Assert.False(new HostFileListing([], null, Truncated: true).IsComplete);
        Assert.False(new HostFileListing([], "X").IsComplete);
    }
}
