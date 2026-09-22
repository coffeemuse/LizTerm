// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public class UssBrowserManageTests
{
    [Fact]
    public async Task New_asks_for_a_name_checks_it_creates_and_selects_the_directory()
    {
        var t = UssTestHost.Create();
        await t.Vm.EnsureListedAsync();

        var creating = t.Vm.NewDirectoryCommand.ExecuteAsync(null);
        await t.AskedAsync(creating);
        var question = t.Ops.Confirmation!;
        Assert.Equal("New directory in /u/ibmuser:", question.Message);
        Assert.Equal("Create", question.PrimaryLabel);
        Assert.True(question.HasInput);
        Assert.False(question.CanAnswerPrimary);
        Assert.Equal("Enter a name.", question.InputProblem);
        question.Input = "a/b";
        Assert.Equal("A name cannot contain '/'.", question.InputProblem);
        question.Input = "..";
        Assert.Equal("A name cannot be '.' or '..'.", question.InputProblem);
        question.Input = "notes";
        Assert.Equal("notes already exists in /u/ibmuser.", question.InputProblem);
        question.Input = "a\tb";
        Assert.Equal("A path cannot contain control characters.", question.InputProblem);
        question.Input = " drafts2 ";
        Assert.Null(question.InputProblem);
        Assert.True(question.CanAnswerPrimary);
        question.PrimaryCommand.Execute(null);
        await creating;

        Assert.Contains("mkdir:/u/ibmuser/drafts2", t.Host.CallsSnapshot());
        Assert.Equal(new[] { "drafts2", "notes", "old" }, t.Vm.Directories.Select(d => d.Name));
        Assert.Equal("drafts2", t.Vm.SelectedDirectory!.Name);
        Assert.Equal("✓ Created /u/ibmuser/drafts2.", t.Ops.StatusText);
    }

    [Fact]
    public async Task New_cancelled_creates_nothing()
    {
        var t = UssTestHost.Create();
        await t.Vm.EnsureListedAsync();
        var creating = t.Vm.NewDirectoryCommand.ExecuteAsync(null);
        await t.AskedAsync(creating);
        t.Ops.Confirmation!.CancelCommand.Execute(null);
        await creating;
        Assert.Equal("– Create cancelled.", t.Ops.StatusText);
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("mkdir:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_name_the_host_already_has_is_reported_and_the_list_refreshed()
    {
        var t = UssTestHost.Create();
        await t.Vm.EnsureListedAsync();
        t.Host.AddDirectory("/u/ibmuser/late");
        var creating = t.Vm.NewDirectoryCommand.ExecuteAsync(null);
        await t.AskedAsync(creating);
        t.Ops.Confirmation!.Input = "late";
        t.Ops.Confirmation.PrimaryCommand.Execute(null);
        await creating;

        Assert.Equal("✗ /u/ibmuser/late: a file or directory of that name already exists.", t.Ops.StatusText);
        Assert.Contains(t.Vm.Directories, d => d.Name == "late");
        Assert.Null(t.Ops.ErrorText);
    }

    [Fact]
    public async Task New_is_off_before_a_listing_and_needs_the_current_directory()
    {
        var t = UssTestHost.Create();
        Assert.False(t.Vm.NewDirectoryCommand.CanExecute(null));
        await t.Vm.EnsureListedAsync();
        Assert.True(t.Vm.NewDirectoryCommand.CanExecute(null));
    }

    [Fact]
    public async Task Delete_directory_asks_names_it_and_drops_the_row()
    {
        var t = UssTestHost.Create();
        await t.Vm.EnsureListedAsync();
        t.Access.Etags.Remember(HostPath.ForUnix("/u/ibmuser/old/x.txt"), "stamp-1");
        t.Access.Etags.Remember(HostPath.ForUnix("/u/ibmuser/notes/todo.md"), "stamp-2");
        t.Vm.SelectedDirectory = t.Vm.Directories.Single(d => d.Name == "old");
        Assert.True(t.Vm.DeleteDirectoryCommand.CanExecute(null));

        var deleting = t.Vm.DeleteDirectoryCommand.ExecuteAsync(null);
        await t.AskedAsync(deleting);
        Assert.Equal("Delete directory old and everything in it? This cannot be undone.", t.Ops.Confirmation!.Message);
        Assert.Equal("Delete old", t.Ops.Confirmation.PrimaryLabel);
        t.Ops.Confirmation.PrimaryCommand.Execute(null);
        await deleting;

        Assert.Contains("delete:/u/ibmuser/old", t.Host.CallsSnapshot());
        Assert.Equal(new[] { "notes" }, t.Vm.Directories.Select(d => d.Name));
        Assert.Null(t.Vm.SelectedDirectory);
        Assert.Equal("1 directory · none selected", t.Vm.DirectoriesFooter);
        Assert.Equal("✓ Deleted /u/ibmuser/old.", t.Ops.StatusText);
        Assert.Equal(1, t.Access.Etags.Count);
        Assert.DoesNotContain("/u/ibmuser/old", t.Host.Directories);
    }

    [Fact]
    public async Task Delete_directory_gone_meanwhile_drops_the_row_with_the_reason()
    {
        var t = UssTestHost.Create();
        await t.Vm.EnsureListedAsync();
        t.Host.Directories.Remove("/u/ibmuser/old");
        t.Vm.SelectedDirectory = t.Vm.Directories.Single(d => d.Name == "old");
        var deleting = t.Vm.DeleteDirectoryCommand.ExecuteAsync(null);
        await t.AskedAsync(deleting);
        t.Ops.Confirmation!.PrimaryCommand.Execute(null);
        await deleting;
        Assert.Equal("✗ /u/ibmuser/old: Not found.", t.Ops.StatusText);
        Assert.DoesNotContain(t.Vm.Directories, d => d.Name == "old");
    }

    [Fact]
    public async Task Delete_files_names_up_to_five_and_removes_them()
    {
        var t = UssTestHost.Create(seed: host =>
        {
            UssTestHost.Standard(host);
            for (var i = 1; i <= 7; i++) host.AddFile($"/u/ibmuser/old/f{i}.txt", "x");
        });
        await t.ListAsync("/u/ibmuser/old");
        t.Select("f1.txt", "f2.txt", "f3.txt", "f4.txt", "f5.txt", "f6.txt", "f7.txt");
        Assert.True(t.Vm.DeleteFilesCommand.CanExecute(null));

        var deleting = t.Vm.DeleteFilesCommand.ExecuteAsync(null);
        await t.AskedAsync(deleting);
        Assert.Equal("Delete f1.txt, f2.txt, f3.txt, f4.txt, f5.txt and 2 more from /u/ibmuser/old? This cannot be undone.", t.Ops.Confirmation!.Message);
        Assert.Equal("Delete 7 files", t.Ops.Confirmation.PrimaryLabel);
        t.Ops.Confirmation.PrimaryCommand.Execute(null);
        await deleting;

        Assert.Empty(t.Vm.Files);
        Assert.Equal("No files", t.Vm.FilesFooter);
        Assert.Equal("✓ Deleted 7 of 7 files.", t.Ops.StatusText);
        Assert.Equal(7, t.Host.CallsSnapshot().Count(c => c.StartsWith("delete:/u/ibmuser/old/", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Delete_files_reports_a_failure_and_goes_on()
    {
        var t = UssTestHost.Create();
        await t.ListAsync("/u/ibmuser/notes");
        t.Select("README.txt", "todo.md");
        t.Host.Failures["delete:/u/ibmuser/notes/README.txt"] = new HostFileException(HostFileErrorKind.ServerError, "/u/ibmuser/notes/README.txt: server error (HTTP 500).");

        var deleting = t.Vm.DeleteFilesCommand.ExecuteAsync(null);
        await t.AskedAsync(deleting);
        Assert.Equal("Delete 2 files", t.Ops.Confirmation!.PrimaryLabel);
        t.Ops.Confirmation.PrimaryCommand.Execute(null);
        await deleting;

        Assert.Equal(new[] { "README.txt", "data.bin" }, t.Vm.Files.Select(f => f.Name));
        Assert.Equal("⚠ Deleted 1 of 2 files. ✗ README.txt: Server error.", t.Ops.StatusText);
    }

    [Fact]
    public async Task Delete_files_stopped_by_a_connection_failure_offers_retry_for_the_rest()
    {
        var t = UssTestHost.Create();
        await t.ListAsync("/u/ibmuser/notes");
        t.Select("README.txt", "todo.md");
        t.Host.Failures["delete:/u/ibmuser/notes/todo.md"] = new HostFileException(HostFileErrorKind.Unreachable, "todo.md: cannot reach the host.");
        var deleting = t.Vm.DeleteFilesCommand.ExecuteAsync(null);
        await t.AskedAsync(deleting);
        t.Ops.Confirmation!.PrimaryCommand.Execute(null);
        await deleting;

        Assert.True(t.Ops.CanRetry);
        Assert.DoesNotContain(t.Vm.Files, f => f.Name == "README.txt");
        Assert.Contains(t.Vm.Files, f => f.Name == "todo.md");
        t.Host.Failures.Clear();
        var retrying = t.Ops.RetryCommand.ExecuteAsync(null);
        await t.AskedAsync(retrying);
        Assert.Equal("Delete todo.md from /u/ibmuser/notes? This cannot be undone.", t.Ops.Confirmation!.Message);
        t.Ops.Confirmation.PrimaryCommand.Execute(null);
        await retrying;
        Assert.Equal(new[] { "data.bin" }, t.Vm.Files.Select(f => f.Name));
        Assert.Equal("✓ Deleted 1 of 1 file.", t.Ops.StatusText);
    }

    [Fact]
    public async Task Delete_files_is_off_for_a_row_that_is_not_a_regular_file()
    {
        var t = UssTestHost.Create();
        await t.ListAsync("/u/ibmuser/notes");
        var link = new FileRow(t.Vm.Current!, new HostFileEntry("link", HostFileEntryKind.Other, Unix: new UnixFileAttributes(0, null)));
        t.Vm.SetSelectedFiles([t.Vm.Files[0], link]);
        Assert.False(t.Vm.DeleteFilesCommand.CanExecute(null));
        t.Select("todo.md");
        Assert.True(t.Vm.DeleteFilesCommand.CanExecute(null));
    }
}
