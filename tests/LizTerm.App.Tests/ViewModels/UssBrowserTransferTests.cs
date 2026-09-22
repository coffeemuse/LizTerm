// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;
using LizTerm.App.ViewModels;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public class UssBrowserTransferTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lizterm-uss-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Local(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string Local(string name, string text) => Local(name, Encoding.UTF8.GetBytes(text));

    private static async Task<UssTestHost> NotesAsync()
    {
        var t = UssTestHost.Create();
        await t.ListAsync("/u/ibmuser/notes");
        return t;
    }

    [Fact]
    public async Task One_file_downloads_to_the_chosen_name_keeping_trailing_blanks_and_remembers_the_stamp()
    {
        var t = await NotesAsync();
        t.Host.Text["/u/ibmuser/notes/README.txt"] = ["hello  ", "world"];
        t.Host.Etags["/u/ibmuser/notes/README.txt"] = "stamp-7";
        t.Select("README.txt");
        var target = Path.Combine(_dir, "README.txt");
        t.Picker.Result = target;

        await t.Vm.DownloadCommand.ExecuteAsync(null);

        Assert.Equal("save:README.txt", t.Picker.Calls.Single());
        Assert.Equal("hello  " + Environment.NewLine + "world" + Environment.NewLine, await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        Assert.Equal($"✓ Downloaded /u/ibmuser/notes/README.txt to {target}.", t.Ops.StatusText);
        Assert.StartsWith("✓ Done · ", t.Vm.Files.Single(f => f.Name == "README.txt").Status);
        Assert.Equal("stamp-7", t.Access.Etags.TryGet(HostPath.ForUnix("/u/ibmuser/notes/README.txt")));
        Assert.Contains("readtext:/u/ibmuser/notes/README.txt", t.Host.CallsSnapshot());
    }

    [Fact]
    public async Task Binary_mode_downloads_the_bytes()
    {
        var t = await NotesAsync();
        t.Vm.IsBinaryMode = true;
        Assert.Equal("Transfer: Binary", t.Vm.TransferModeLabel);
        t.Select("data.bin");
        var target = Path.Combine(_dir, "data.bin");
        t.Picker.Result = target;

        await t.Vm.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
        Assert.Contains("readbinary:/u/ibmuser/notes/data.bin", t.Host.CallsSnapshot());
    }

    [Fact]
    public async Task Several_files_download_into_a_folder_and_an_existing_one_asks()
    {
        var t = await NotesAsync();
        File.WriteAllText(Path.Combine(_dir, "todo.md"), "old");
        t.Select("README.txt", "todo.md");
        t.Picker.FolderResult = _dir;

        var downloading = t.Vm.DownloadCommand.ExecuteAsync(null);
        await t.AskedAsync(downloading);
        Assert.Equal($"todo.md already exists in {_dir}.", t.Ops.Confirmation!.Message);
        Assert.Equal("Replace", t.Ops.Confirmation.PrimaryLabel);
        Assert.Equal("Skip", t.Ops.Confirmation.SecondaryLabel);
        t.Ops.Confirmation.SecondaryCommand.Execute(null);
        await downloading;

        Assert.Equal("folder:Download 2 files from /u/ibmuser/notes", t.Picker.Calls.Single());
        Assert.Equal("– Skipped: the file exists", t.Vm.Files.Single(f => f.Name == "todo.md").Status);
        Assert.Equal("✓ Done · 12 bytes", t.Vm.Files.Single(f => f.Name == "README.txt").Status);
        Assert.Equal("old", File.ReadAllText(Path.Combine(_dir, "todo.md")));
        Assert.Equal($"⚠ Downloaded 1 of 2 files to {_dir}.", t.Ops.StatusText);
    }

    [Fact]
    public async Task Download_is_off_without_a_regular_file_selected_and_a_cancelled_picker_does_nothing()
    {
        var t = await NotesAsync();
        Assert.False(t.Vm.DownloadCommand.CanExecute(null));
        t.Select("todo.md");
        Assert.True(t.Vm.DownloadCommand.CanExecute(null));
        t.Picker.Result = null;
        var before = t.Ops.StatusText;
        await t.Vm.DownloadCommand.ExecuteAsync(null);
        Assert.Equal(before, t.Ops.StatusText);
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("readtext:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Upload_sends_new_files_under_their_own_names_and_names_the_refused_ones()
    {
        var t = await NotesAsync();
        t.Picker.Results = [Local("new.txt", "a\tb\nc  \n"), Local("bad.bin", [0xFF, 0xFE, 0x00])];

        await t.Vm.UploadCommand.ExecuteAsync(null);

        Assert.Equal("open-many:Upload to /u/ibmuser/notes", t.Picker.Calls.Single());
        Assert.Equal(new[] { "a\tb", "c  " }, t.Host.Text["/u/ibmuser/notes/new.txt"]);
        Assert.Contains("writetext:/u/ibmuser/notes/new.txt:2", t.Host.CallsSnapshot());
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.Contains("bad.bin"));
        Assert.Equal("✓ Uploaded and verified", t.Vm.Files.Single(f => f.Name == "new.txt").Status);
        Assert.Equal("⚠ Uploaded 1 of 2 files to /u/ibmuser/notes. ✗ Not sent: bad.bin: The file is not UTF-8 text. Choose Binary to send its bytes unchanged.", t.Ops.StatusText);
        Assert.NotNull(t.Access.Etags.TryGet(HostPath.ForUnix("/u/ibmuser/notes/new.txt")));
    }

    [Fact]
    public async Task Upload_over_an_existing_file_asks_and_skip_leaves_it()
    {
        var t = await NotesAsync();
        t.Picker.Results = [Local("README.txt", "replaced\n"), Local("todo.md", "replaced\n")];

        var uploading = t.Vm.UploadCommand.ExecuteAsync(null);
        await t.AskedAsync(uploading);
        Assert.Equal("README.txt already exists in /u/ibmuser/notes.", t.Ops.Confirmation!.Message);
        Assert.True(t.Ops.Confirmation.OffersApplyToAll);
        t.Ops.Confirmation.PrimaryCommand.Execute(null);
        await t.AskedAsync(uploading);
        Assert.Equal("todo.md already exists in /u/ibmuser/notes.", t.Ops.Confirmation!.Message);
        t.Ops.Confirmation.SecondaryCommand.Execute(null);
        await uploading;

        Assert.Equal(new[] { "replaced" }, t.Host.Text["/u/ibmuser/notes/README.txt"]);
        Assert.Equal(new[] { "- x" }, t.Host.Text["/u/ibmuser/notes/todo.md"]);
        Assert.Equal("– Skipped: the file exists", t.Vm.Files.Single(f => f.Name == "todo.md").Status);
        Assert.Equal("✓ Uploaded and verified", t.Vm.Files.Single(f => f.Name == "README.txt").Status);
        Assert.Equal("⚠ Uploaded 1 of 2 files to /u/ibmuser/notes.", t.Ops.StatusText);
    }

    [Fact]
    public async Task A_remembered_stamp_goes_out_and_a_conflict_asks()
    {
        var t = await NotesAsync();
        var path = HostPath.ForUnix("/u/ibmuser/notes/README.txt");
        t.Host.Etags["/u/ibmuser/notes/README.txt"] = "stamp-1";
        t.Access.Etags.Remember(path, "stale");
        t.Picker.Results = [Local("README.txt", "replaced\n")];

        var uploading = t.Vm.UploadCommand.ExecuteAsync(null);
        await t.AskedAsync(uploading);
        t.Ops.Confirmation!.PrimaryCommand.Execute(null);
        await t.AskedAsync(uploading);
        Assert.Equal("README.txt changed on the host since you downloaded it.", t.Ops.Confirmation!.Message);
        Assert.Equal("Replace anyway", t.Ops.Confirmation.PrimaryLabel);
        t.Ops.Confirmation.PrimaryCommand.Execute(null);
        await uploading;

        Assert.Equal(new string?[] { "stale", null }, t.Host.IfMatches);
        Assert.Equal(new[] { "replaced" }, t.Host.Text["/u/ibmuser/notes/README.txt"]);
        Assert.Equal("✓ Uploaded 1 of 1 file to /u/ibmuser/notes.", t.Ops.StatusText);
        Assert.Equal(t.Host.Etags["/u/ibmuser/notes/README.txt"], t.Access.Etags.TryGet(path));
    }

    [Fact]
    public async Task A_file_over_the_cap_is_refused_before_any_request()
    {
        var t = await NotesAsync();
        t.Vm.IsBinaryMode = true;
        t.Picker.Results = [Local("big.bin", new byte[HostFileLimits.MaxUnixFileBytes + 1])];

        await t.Vm.UploadCommand.ExecuteAsync(null);

        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("writebinary:", StringComparison.Ordinal));
        Assert.Equal("⚠ Uploaded 0 of 1 file to /u/ibmuser/notes. ✗ Not sent: big.bin: The file is 1,048,577 bytes; the host holds at most 1,048,576.", t.Ops.StatusText);
    }

    [Fact]
    public async Task Verify_off_says_uploaded_and_a_differing_copy_is_a_warning()
    {
        var t = await NotesAsync();
        t.Vm.VerifyUploads = false;
        t.Picker.Results = [Local("one.txt", "x\n")];
        await t.Vm.UploadCommand.ExecuteAsync(null);
        Assert.Equal("✓ Uploaded", t.Vm.Files.Single(f => f.Name == "one.txt").Status);

        t.Vm.VerifyUploads = true;
        t.Host.StoreTransform = (_, lines) => [.. lines.Select(l => l + "!")];
        t.Picker.Results = [Local("two.txt", "y\n")];
        await t.Vm.UploadCommand.ExecuteAsync(null);
        Assert.Equal("⚠ Uploaded, but the host copy differs at line 1", t.Vm.Files.Single(f => f.Name == "two.txt").Status);
        Assert.Equal("⚠ Uploaded 1 of 1 file to /u/ibmuser/notes.", t.Ops.StatusText);
    }

    [Fact]
    public async Task An_upload_stopped_by_a_connection_failure_offers_retry_for_the_rest()
    {
        var t = await NotesAsync();
        t.Host.Failures["writetext:/u/ibmuser/notes/b.txt"] = new HostFileException(HostFileErrorKind.Unreachable, "b.txt: cannot reach the host.");
        t.Picker.Results = [Local("a.txt", "a\n"), Local("b.txt", "b\n"), Local("c.txt", "c\n")];

        await t.Vm.UploadCommand.ExecuteAsync(null);

        Assert.True(t.Ops.CanRetry);
        Assert.Equal("b.txt: cannot reach the host. The file may be partly written.", t.Ops.ErrorText);
        Assert.True(t.Host.Text.ContainsKey("/u/ibmuser/notes/a.txt"));
        t.Host.Failures.Clear();
        await t.Ops.RetryCommand.ExecuteAsync(null);
        Assert.True(t.Host.Text.ContainsKey("/u/ibmuser/notes/b.txt"));
        Assert.True(t.Host.Text.ContainsKey("/u/ibmuser/notes/c.txt"));
        Assert.Equal(1, t.Host.CallsSnapshot().Count(c => c == "writetext:/u/ibmuser/notes/a.txt:1"));
        Assert.Equal("✓ Uploaded 2 of 2 files to /u/ibmuser/notes.", t.Ops.StatusText);
    }

    [Fact]
    public async Task View_reads_one_file_as_text_without_a_stamp()
    {
        var t = await NotesAsync();
        Assert.False(t.Vm.ViewCommand.CanExecute(null));
        t.Select("README.txt", "todo.md");
        Assert.False(t.Vm.ViewCommand.CanExecute(null));
        t.Select("README.txt");
        Assert.True(t.Vm.ViewCommand.CanExecute(null));
        t.Vm.ViewGestureText = "⌘⏎";
        Assert.Equal("View the selected file (⌘⏎)", t.Vm.ViewHint);

        await t.Vm.ViewCommand.ExecuteAsync(null);

        Assert.Equal("/u/ibmuser/notes/README.txt", t.Vm.Viewer!.Path);
        Assert.Equal("hello\nworld", t.Vm.Viewer.Text);
        Assert.False(t.Vm.Viewer.TrimmedTrailingBlanks);
        Assert.Equal("✓ Read /u/ibmuser/notes/README.txt · 2 lines.", t.Ops.StatusText);
        Assert.Empty(t.Host.EtagRequests);
        Assert.Equal("", t.Vm.Files.Single(f => f.Name == "README.txt").Status);
    }
}
