// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public class UssBrowserListingTests
{
    [Theory]
    [InlineData("IBMUSER", "/u/ibmuser")]
    [InlineData(" Mvsce02 ", "/u/mvsce02")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("a\tb", "/")]
    public void The_start_path_is_the_home_directory_in_lower_case_or_the_root(string? userid, string expected) =>
        Assert.Equal(expected, UssBrowserViewModel.StartPath(userid));

    [Fact]
    public async Task The_first_show_lists_the_start_path_once()
    {
        var t = UssTestHost.Create();
        Assert.Equal("/u/ibmuser", t.Vm.Path);
        Assert.Null(t.Vm.Current);
        Assert.Equal("", t.Vm.DirectoriesFooter);
        Assert.Equal("Files", t.Vm.FilesTitle);

        await t.Vm.EnsureListedAsync();
        await t.Vm.EnsureListedAsync();

        Assert.Equal("/u/ibmuser", t.Vm.Current!.UnixPath);
        Assert.Equal(new[] { "notes", "old" }, t.Vm.Directories.Select(d => d.Name));
        Assert.Empty(t.Vm.Files);
        Assert.Equal("2 directories · none selected", t.Vm.DirectoriesFooter);
        Assert.Equal("No files", t.Vm.FilesFooter);
        Assert.Equal("/u/ibmuser", t.Vm.FilesTitle);
        Assert.Equal("✓ Listed /u/ibmuser · 2 directories, 0 files.", t.Ops.StatusText);
        Assert.Equal(1, t.Host.CallsSnapshot().Count(c => c == "listdir:/u/ibmuser"));
    }

    [Fact]
    public async Task Go_lists_a_typed_path_and_writes_it_back_trimmed()
    {
        var t = UssTestHost.Create();

        await t.ListAsync("  /u/ibmuser/notes ");

        Assert.Equal("/u/ibmuser/notes", t.Vm.Path);
        Assert.Equal("/u/ibmuser/notes", t.Vm.Current!.UnixPath);
        Assert.Equal(new[] { "drafts" }, t.Vm.Directories.Select(d => d.Name));
        Assert.Equal(new[] { "README.txt", "data.bin", "todo.md" }, t.Vm.Files.Select(f => f.Name));
        Assert.Equal("12", t.Vm.Files[0].Size);
        Assert.Equal("3", t.Vm.Files[1].Size);
        Assert.All(t.Vm.Files, f => Assert.True(f.IsFile));
        Assert.Equal("/u/ibmuser/notes/README.txt", t.Vm.Files[0].Path!.UnixPath);
        Assert.Equal("1 directory · none selected", t.Vm.DirectoriesFooter);
        Assert.Equal("3 files · none selected", t.Vm.FilesFooter);
        Assert.Equal("✓ Listed /u/ibmuser/notes · 1 directory, 3 files.", t.Ops.StatusText);
    }

    [Fact]
    public async Task Rows_are_sorted_by_name_whatever_order_the_host_used()
    {
        var t = UssTestHost.Create();
        await t.ListAsync("/u/ibmuser/notes");
        var sorted = t.Vm.Files.Select(f => f.Name).ToList();
        Assert.Equal(sorted.OrderBy(n => n, StringComparer.Ordinal), sorted);
    }

    [Fact]
    public async Task Opening_a_directory_descends_and_up_climbs_until_the_root()
    {
        var t = UssTestHost.Create();
        await t.Vm.EnsureListedAsync();
        t.Vm.SelectedDirectory = t.Vm.Directories.Single(d => d.Name == "notes");
        Assert.True(t.Vm.OpenDirectoryCommand.CanExecute(null));

        await t.Vm.OpenDirectoryCommand.ExecuteAsync(null);
        Assert.Equal("/u/ibmuser/notes", t.Vm.Current!.UnixPath);
        Assert.Null(t.Vm.SelectedDirectory);

        await t.Vm.UpCommand.ExecuteAsync(null);
        Assert.Equal("/u/ibmuser", t.Vm.Current!.UnixPath);
        await t.Vm.UpCommand.ExecuteAsync(null);
        await t.Vm.UpCommand.ExecuteAsync(null);
        Assert.Equal("/", t.Vm.Current!.UnixPath);
        Assert.Equal(new[] { "tmp", "u" }, t.Vm.Directories.Select(d => d.Name));
        Assert.False(t.Vm.UpCommand.CanExecute(null));
        Assert.False(t.Vm.OpenDirectoryCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("u/ibmuser", "✗ A path must start with '/'.")]
    [InlineData("MVSCE02.CNTL", "✗ A path must start with '/'.")]
    [InlineData("", "✗ Enter a path.")]
    [InlineData("/u/../x", "✗ A path cannot contain a '.' or '..' segment.")]
    public async Task A_path_the_rules_refuse_is_a_status_line_and_no_request(string typed, string expected)
    {
        var t = UssTestHost.Create();
        await t.Vm.EnsureListedAsync();
        var calls = t.Host.CallsSnapshot().Length;

        await t.ListAsync(typed);

        Assert.Equal(expected, t.Ops.StatusText);
        Assert.Equal("/u/ibmuser", t.Vm.Current!.UnixPath);
        Assert.Equal(typed, t.Vm.Path);
        Assert.Equal(calls, t.Host.CallsSnapshot().Length);
    }

    [Fact]
    public async Task A_missing_path_keeps_the_last_listing_and_the_typed_text()
    {
        var t = UssTestHost.Create();
        await t.Vm.EnsureListedAsync();

        await t.ListAsync("/u/nobody");

        Assert.Equal("✗ File not found: /u/nobody", t.Ops.StatusText);
        Assert.Equal("/u/nobody", t.Vm.Path);
        Assert.Equal("/u/ibmuser", t.Vm.Current!.UnixPath);
        Assert.Equal(new[] { "notes", "old" }, t.Vm.Directories.Select(d => d.Name));
        Assert.Null(t.Ops.ErrorText);
    }

    [Fact]
    public async Task A_file_path_is_refused_by_the_host_and_reported()
    {
        var t = UssTestHost.Create();
        await t.ListAsync("/u/ibmuser/notes/todo.md");
        Assert.StartsWith("✗ /u/ibmuser/notes/todo.md: The host refused the request", t.Ops.StatusText);
        Assert.Null(t.Vm.Current);
    }

    [Fact]
    public async Task A_connection_failure_is_the_banner_and_retry_lists()
    {
        var t = UssTestHost.Create();
        t.Host.Failures["listdir:/u/ibmuser"] = new HostFileException(HostFileErrorKind.Unreachable, "/u/ibmuser: cannot reach the host.");

        await t.Vm.EnsureListedAsync();

        Assert.True(t.Ops.HasError);
        Assert.True(t.Ops.CanRetry);
        Assert.Null(t.Vm.Current);
        t.Host.Failures.Clear();
        await t.Ops.RetryCommand.ExecuteAsync(null);
        Assert.False(t.Ops.HasError);
        Assert.Equal("/u/ibmuser", t.Vm.Current!.UnixPath);
    }

    [Fact]
    public async Task Refresh_lists_again_and_keeps_the_selection_by_name()
    {
        var t = UssTestHost.Create();
        await t.ListAsync("/u/ibmuser/notes");
        t.Vm.SelectedDirectory = t.Vm.Directories[0];
        t.Select("todo.md", "data.bin");
        IReadOnlyList<FileRow>? requested = null;
        t.Vm.SelectFilesRequested += rows => requested = rows;
        t.Host.AddFile("/u/ibmuser/notes/new.txt", "n");

        await t.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "README.txt", "data.bin", "new.txt", "todo.md" }, t.Vm.Files.Select(f => f.Name));
        Assert.Equal("drafts", t.Vm.SelectedDirectory!.Name);
        Assert.Equal(new[] { "data.bin", "todo.md" }, requested!.Select(f => f.Name));
        Assert.Equal(new[] { "data.bin", "todo.md" }, t.Vm.SelectedFiles.Select(f => f.Name));
        Assert.Equal("4 files · 2 selected", t.Vm.FilesFooter);
        Assert.Equal("1 directory · 1 selected", t.Vm.DirectoriesFooter);
    }

    [Fact]
    public async Task The_commands_are_off_while_an_operation_runs()
    {
        var t = UssTestHost.Create();
        t.Host.Gate = new TaskCompletionSource();
        var listing = t.Vm.EnsureListedAsync();
        await Wait.UntilAsync(() => t.Vm.IsBusy, "the listing to start");

        Assert.False(t.Vm.GoCommand.CanExecute(null));
        Assert.False(t.Vm.RefreshCommand.CanExecute(null));
        Assert.False(t.Vm.UpCommand.CanExecute(null));
        t.Host.Gate.SetResult();
        await listing;
        Assert.True(t.Vm.GoCommand.CanExecute(null));
        Assert.True(t.Vm.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public void A_row_that_is_not_a_regular_file_or_whose_name_the_rules_refuse_says_so()
    {
        var parent = HostPath.ForUnix("/u/ibmuser");
        var link = new FileRow(parent, new HostFileEntry("link", HostFileEntryKind.Other, Unix: new UnixFileAttributes(0, null)));
        Assert.False(link.IsFile);
        Assert.Equal("link (not a file)", link.DisplayName);
        Assert.Equal("/u/ibmuser/link", link.Path!.UnixPath);

        var bad = new FileRow(parent, new HostFileEntry("a\tb", HostFileEntryKind.File, Unix: new UnixFileAttributes(5, null)));
        Assert.False(bad.IsFile);
        Assert.Null(bad.Path);
        Assert.Equal("a\tb (not usable)", bad.DisplayName);

        var directory = new DirectoryRow(parent, new HostFileEntry("a\tb", HostFileEntryKind.Directory, Unix: new UnixFileAttributes(128, null)));
        Assert.False(directory.IsUsable);
        Assert.Equal("a\tb (not usable)", directory.DisplayName);

        var when = new DateTimeOffset(2026, 9, 22, 9, 12, 0, TimeSpan.Zero);
        var file = new FileRow(parent, new HostFileEntry("f", HostFileEntryKind.File, Unix: new UnixFileAttributes(1204, when)));
        Assert.Equal("1,204", file.Size);
        Assert.Equal(when.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture), file.Modified);
        Assert.Equal("", new FileRow(parent, new HostFileEntry("g", HostFileEntryKind.File)).Modified);
    }
}
