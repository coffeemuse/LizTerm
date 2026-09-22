// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Text;
using LizTerm.Core.HostFiles;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfUnixTests
{
    private static readonly HostPath Home = HostPath.ForUnix("/u/ibmuser");
    private static readonly HostPath Fix = HostPath.ForUnix("/u/ibmuser/liztest-fix");
    private static readonly HostPath Hello = HostPath.ForUnix("/u/ibmuser/liztest-fix/hello.txt");

    private static MvsmfFileService Service(RecordedHandler handler) =>
        new(handler, MvsmfAuthTests.Base, MvsmfAuthTests.Providing([], new HostCredentials("IBMUSER", "pw")));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_directory_listing_reads_kinds_sizes_and_times()
    {
        var handler = new RecordedHandler().Then("login-200").Then("uss-list-root");
        using var service = Service(handler);

        var listing = await service.ListDirectoryAsync(HostPath.ForUnix("/"), HostListRequest.All, Ct);

        Assert.True(listing.IsComplete);
        Assert.False(listing.Truncated);
        Assert.Equal(new[] { "tmp", "u", "www" }, listing.Entries.Select(e => e.Name));
        Assert.All(listing.Entries, e => Assert.Equal(HostFileEntryKind.Directory, e.Kind));
        Assert.All(listing.Entries, e => Assert.Null(e.Attributes));
        var u = listing.Entries[1].Unix!;
        Assert.Equal(128, u.Size);
        Assert.Equal(new DateTimeOffset(2026, 9, 17, 20, 36, 34, TimeSpan.Zero), u.Modified);
        var request = handler.Requests[1];
        Assert.Equal("/zosmf/restfiles/fs?path=/", request.Uri.PathAndQuery);
        Assert.Equal("0", request.Headers["X-IBM-Max-Items"]);
    }

    [Fact]
    public async Task An_empty_directory_lists_nothing()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("uss-list-empty"));

        var listing = await service.ListDirectoryAsync(Home, HostListRequest.All, Ct);

        Assert.Empty(listing.Entries);
        Assert.True(listing.IsComplete);
    }

    [Fact]
    public async Task A_file_in_a_listing_is_a_file_entry()
    {
        var handler = new RecordedHandler().Then("login-200").Then(HttpStatusCode.OK,
            """{"items":[{"name":"notes.txt","mode":"-rw-r--r--","size":1204,"user":"","group":"","links":1,"mtime":"2026-09-22T09:12:00Z","inode":9},{"name":"drafts","mode":"drwxr-xr-x","size":128,"user":"","group":"","links":2,"mtime":"not a date","inode":10}],"returnedRows":2,"totalRows":2,"JSONversion":1}""");
        using var service = Service(handler);

        var entries = (await service.ListDirectoryAsync(Home, HostListRequest.All, Ct)).Entries;

        Assert.Equal(HostFileEntryKind.File, entries[0].Kind);
        Assert.Equal(1204, entries[0].Unix!.Size);
        Assert.Equal(HostFileEntryKind.Directory, entries[1].Kind);
        Assert.Null(entries[1].Unix!.Modified);
    }

    [Fact]
    public async Task Uss_list_no_continuation_so_a_cut_listing_is_truncated_and_a_continuation_is_refused()
    {
        var handler = new RecordedHandler().Then("login-200").Then("uss-list-truncated");
        using var service = Service(handler);

        var listing = await service.ListDirectoryAsync(HostPath.ForUnix("/u"), new HostListRequest(MaxItems: 1), Ct);

        Assert.Single(listing.Entries);
        Assert.True(listing.Truncated);
        Assert.False(listing.IsComplete);
        Assert.Null(listing.Continuation);
        Assert.Equal("1", handler.Requests[1].Headers["X-IBM-Max-Items"]);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListDirectoryAsync(HostPath.ForUnix("/u"), new HostListRequest(MaxItems: 1, Continuation: "ibmuser"), Ct));
    }

    [Fact]
    public async Task A_host_that_ignores_the_limit_is_still_reported_as_truncated()
    {
        const string threeItems = """{"items":[{"name":"one.txt","mode":"-rw-r--r--","size":1,"user":"","group":"","links":1,"mtime":"2026-09-22T09:12:00Z","inode":1},{"name":"two.txt","mode":"-rw-r--r--","size":2,"user":"","group":"","links":1,"mtime":"2026-09-22T09:12:00Z","inode":2},{"name":"three.txt","mode":"-rw-r--r--","size":3,"user":"","group":"","links":1,"mtime":"2026-09-22T09:12:00Z","inode":3}],"returnedRows":3,"totalRows":3,"JSONversion":1}""";

        using var trimmed = Service(new RecordedHandler().Then("login-200").Then(HttpStatusCode.OK, threeItems));
        var cut = await trimmed.ListDirectoryAsync(Home, new HostListRequest(MaxItems: 2), Ct);
        Assert.Equal(2, cut.Entries.Count);
        Assert.True(cut.Truncated);
        Assert.False(cut.IsComplete);
        Assert.Null(cut.Continuation);

        using var whole = Service(new RecordedHandler().Then("login-200").Then(HttpStatusCode.OK, threeItems));
        var untrimmed = await whole.ListDirectoryAsync(Home, new HostListRequest(MaxItems: 5), Ct);
        Assert.Equal(3, untrimmed.Entries.Count);
        Assert.False(untrimmed.Truncated);
    }

    [Fact]
    public async Task A_missing_directory_is_not_found()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("uss-list-missing"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.ListDirectoryAsync(HostPath.ForUnix("/u/nobody"), HostListRequest.All, Ct));

        Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);
        Assert.Equal("/u/nobody: not found.", ex.Message);
    }

    [Fact]
    public async Task Uss_stat_for_file_path_so_listing_a_file_is_an_invalid_request()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("uss-stat-file"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.ListDirectoryAsync(Hello, HostListRequest.All, Ct));

        Assert.Equal(HostFileErrorKind.InvalidRequest, ex.Kind);
        Assert.Equal("/u/ibmuser/liztest-fix/hello.txt: is a file, not a directory.", ex.Message);
    }

    [Fact]
    public async Task A_text_read_routes_to_fs_and_keeps_latin1_with_the_stamp_when_asked()
    {
        var handler = new RecordedHandler().Then("login-200").Then("uss-read-text");
        using var service = Service(handler);

        var read = await service.ReadTextAsync(Hello, withEtag: true, cancellationToken: Ct);

        Assert.Equal(new[] { "hello", "", "¬ end" }, read.Lines);
        Assert.Matches("^[0-9A-Fa-f]{16}$", read.Etag);
        var request = handler.Requests[1];
        Assert.Equal("/zosmf/restfiles/fs/u/ibmuser/liztest-fix/hello.txt", request.Uri.PathAndQuery);
        Assert.Equal("text", request.DataType);
        Assert.Equal("true", request.Headers["X-IBM-Return-Etag"]);
    }

    [Fact]
    public async Task A_binary_read_gives_the_same_stamp_as_the_text_read()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("uss-read-text").Then("uss-read-binary"));
        var text = await service.ReadTextAsync(Hello, withEtag: true, cancellationToken: Ct);
        using var bytes = new MemoryStream();

        var binary = await service.ReadBinaryAsync(Hello, bytes, withEtag: true, cancellationToken: Ct);

        Assert.Equal(text.Etag, binary.Etag);
        Assert.Equal(bytes.Length, binary.Bytes);
        Assert.True(binary.Bytes > 0);
    }

    [Fact]
    public async Task A_missing_file_is_not_found_and_a_directory_is_an_invalid_request()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("uss-read-missing").Then("uss-read-directory"));

        var missing = await Assert.ThrowsAsync<HostFileException>(() => service.ReadTextAsync(Fix.Child("nope.txt"), cancellationToken: Ct));
        var directory = await Assert.ThrowsAsync<HostFileException>(() => service.ReadTextAsync(HostPath.ForUnix("/u"), cancellationToken: Ct));

        Assert.Equal(HostFileErrorKind.NotFound, missing.Kind);
        Assert.Equal(HostFileErrorKind.InvalidRequest, directory.Kind);
        Assert.Equal("Is a directory", directory.ServerMessage);
    }

    [Fact]
    public async Task A_text_write_puts_latin1_lines_to_fs_and_returns_the_stamp()
    {
        var handler = new RecordedHandler().Then("login-200").Then("uss-write-etag-204");
        using var service = Service(handler);

        var etag = await service.WriteTextAsync(Hello, ["hello", "", "¬ end"], cancellationToken: Ct);

        Assert.Matches("^[0-9A-Fa-f]{16}$", etag);
        var request = handler.Requests[1];
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/zosmf/restfiles/fs/u/ibmuser/liztest-fix/hello.txt", request.Uri.PathAndQuery);
        Assert.Equal("text/plain", request.ContentType);
        Assert.Equal("text", request.DataType);
        Assert.Equal(new byte[] { (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', (byte)'\n', (byte)'\n', 0xAC, (byte)' ', (byte)'e', (byte)'n', (byte)'d', (byte)'\n' }, request.Body);
        Assert.Equal("true", request.Headers["X-IBM-Return-Etag"]);
    }

    [Fact]
    public async Task A_binary_write_sends_octet_stream_and_if_match_verbatim()
    {
        var handler = new RecordedHandler().Then("login-200").Then("uss-write-204");
        using var service = Service(handler);

        await service.WriteBinaryAsync(Fix.Child("data.bin"), new MemoryStream([1, 2, 3]), ifMatch: "ABCDEF0123456789", Ct);

        var request = handler.Requests[1];
        Assert.Equal("/zosmf/restfiles/fs/u/ibmuser/liztest-fix/data.bin", request.Uri.PathAndQuery);
        Assert.Equal("application/octet-stream", request.ContentType);
        Assert.Equal("binary", request.DataType);
        Assert.Equal(new byte[] { 1, 2, 3 }, request.Body);
        Assert.Equal("ABCDEF0123456789", request.Headers["If-Match"]);
    }

    [Fact]
    public async Task A_stale_stamp_is_a_conflict()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("uss-write-412"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.WriteTextAsync(Hello, ["x"], ifMatch: "0000000000000000", Ct));

        Assert.Equal(HostFileErrorKind.Conflict, ex.Kind);
        Assert.Equal(1, ex.Reason);
    }

    [Fact]
    public async Task Uss_limits_so_a_write_past_the_hosts_ceiling_is_refused_with_nothing_written()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("uss-write-too-large"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.WriteBinaryAsync(Fix.Child("big.bin"), new MemoryStream(new byte[2_200_000]), cancellationToken: Ct));

        Assert.Equal(HostFileErrorKind.InvalidRequest, ex.Kind);
        Assert.Equal("Failed to read request body", ex.ServerMessage);
        Assert.Equal(1_048_576, HostFileLimits.MaxUnixFileBytes);
    }

    [Fact]
    public async Task A_write_past_64_KB_is_stored_whole()
    {
        var handler = new RecordedHandler().Then("login-200").Then("uss-write-70000-204");
        using var service = Service(handler);

        await service.WriteBinaryAsync(Fix.Child("big.bin"), new MemoryStream(new byte[70_000]), cancellationToken: Ct);

        Assert.Equal(70_000, handler.Requests[1].Body!.Length);
    }

    [Fact]
    public async Task A_directory_is_created_with_a_json_post()
    {
        var handler = new RecordedHandler().Then("login-200").Then("uss-mkdir-201");
        using var service = Service(handler);

        await service.CreateDirectoryAsync(Fix, Ct);

        var request = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/zosmf/restfiles/fs/u/ibmuser/liztest-fix", request.Uri.PathAndQuery);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("""{"type":"directory"}""", request.BodyText);
    }

    [Fact]
    public async Task Uss_create_errors_400_so_an_existing_name_already_exists()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("uss-mkdir-exists"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.CreateDirectoryAsync(Fix, Ct));

        Assert.Equal(HostFileErrorKind.AlreadyExists, ex.Kind);
        Assert.Equal("/u/ibmuser/liztest-fix: a file or directory of that name already exists.", ex.Message);
    }

    [Fact]
    public async Task Creating_under_a_missing_parent_is_not_found()
    {
        using var service = Service(new RecordedHandler().Then("login-200").Then("uss-mkdir-no-parent"));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.CreateDirectoryAsync(HostPath.ForUnix("/u/nobody/child"), Ct));

        Assert.Equal(HostFileErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public async Task A_delete_sends_recursive_for_every_unix_path()
    {
        var handler = new RecordedHandler().Then("login-200").Then("uss-delete-204").Then("uss-delete-missing");
        using var service = Service(handler);

        await service.DeleteAsync(Fix, Ct);
        var gone = await Assert.ThrowsAsync<HostFileException>(() => service.DeleteAsync(Fix, Ct));

        var request = handler.Requests[1];
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/zosmf/restfiles/fs/u/ibmuser/liztest-fix", request.Uri.PathAndQuery);
        Assert.Equal("recursive", request.Headers["X-IBM-Option"]);
        Assert.Equal(HostFileErrorKind.NotFound, gone.Kind);
    }

    [Fact]
    public async Task A_dataset_delete_sends_no_option()
    {
        var handler = new RecordedHandler().Then("login-200").Then("delete-204");
        using var service = Service(handler);

        await service.DeleteAsync(HostPath.ForMember("MVSCE02.CNTL", "NEWMEM"), Ct);

        Assert.DoesNotContain("X-IBM-Option", handler.Requests[1].HeaderNames, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/u/ibmuser/hello world#1.txt", "/u/ibmuser/hello%20world%231.txt")]
    [InlineData("/u/ibmuser/100%.txt", "/u/ibmuser/100%25.txt")]
    [InlineData("/u/ibmuser/a?b&c=d", "/u/ibmuser/a%3Fb%26c%3Dd")]
    [InlineData("/u/ibmuser/Notes-1_2.txt~", "/u/ibmuser/Notes-1_2.txt~")]
    [InlineData("/café", "/caf%C3%A9")]
    public void A_unix_path_is_escaped_byte_by_byte_keeping_slashes(string path, string expected) =>
        Assert.Equal(expected, MvsmfFileService.EscapeUnixPath(path));

    [Fact]
    public async Task A_listing_query_carries_the_escaped_path()
    {
        var handler = new RecordedHandler().Then("login-200").Then("uss-list-empty");
        using var service = Service(handler);

        await service.ListDirectoryAsync(HostPath.ForUnix("/u/ibmuser/my notes"), HostListRequest.All, Ct);

        Assert.Equal("/zosmf/restfiles/fs?path=/u/ibmuser/my%20notes", handler.Requests[1].Uri.PathAndQuery);
    }

    [Fact]
    public async Task The_dataset_verbs_refuse_a_unix_path_and_rename_refuses_it_before_any_request()
    {
        var handler = new RecordedHandler().Then("login-200");
        using var service = Service(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ListMembersAsync(Home, HostListRequest.All, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateDatasetAsync(Home,
            new DatasetAllocation(DatasetOrganization.Sequential, "FB", 80, 3120, SpaceUnit.Tracks, 1, 1, 0), Ct));
        var rename = await Assert.ThrowsAsync<ArgumentException>(() => service.RenameAsync(Hello, "other.txt", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ListDirectoryAsync(HostPath.ForDataset("SYS1.PROCLIB"), HostListRequest.All, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateDirectoryAsync(HostPath.ForDataset("SYS1.PROCLIB"), Ct));

        Assert.StartsWith("The host cannot rename a UNIX file.", rename.Message);
        Assert.Empty(handler.Requests);
    }
}
