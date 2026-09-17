// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using System.Text;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public sealed class MvsmfBrowserDownloadTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lizterm-browser-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Local(string name) => Path.Combine(_folder, name);

    private static async Task<BrowserTestHost> ChosenAsync(string dataset = "MVSCE02.CNTL")
    {
        var t = BrowserTestHost.Create();
        foreach (var member in new[] { "ALLOC", "COMPILE", "HELLO" })
            t.Host.Text[$"MVSCE02.CNTL({member})"] = [$"//{member} JOB   ", "//STEP EXEC PGM=IEFBR14"];
        t.Host.Binary["MVSCE02.UFSHOME"] = [0x61, 0x61, 0x00];
        await t.ChooseAsync(dataset);
        return t;
    }

    [Fact]
    public async Task One_member_downloads_through_the_save_dialog_as_text()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");
        t.Picker.Result = Local("hello.txt");

        await t.Vm.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "save:HELLO.txt" }, t.Picker.Calls);
        var expected = "//HELLO JOB" + Environment.NewLine + "//STEP EXEC PGM=IEFBR14" + Environment.NewLine;
        Assert.Equal(expected, await File.ReadAllTextAsync(Local("hello.txt"), TestContext.Current.CancellationToken));
        var bytes = Encoding.UTF8.GetByteCount(expected);
        Assert.Equal($"✓ Done · {bytes.ToString("N0", CultureInfo.InvariantCulture)} bytes", t.Vm.Members.Single(m => m.Name == "HELLO").Status);
        Assert.Equal($"✓ Downloaded MVSCE02.CNTL(HELLO) to {Local("hello.txt")}.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Trailing_blanks_can_be_kept()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");
        t.Vm.TrimTrailingBlanks = false;
        t.Picker.Result = Local("hello.txt");

        await t.Vm.DownloadCommand.ExecuteAsync(null);

        Assert.StartsWith("//HELLO JOB   " + Environment.NewLine, await File.ReadAllTextAsync(Local("hello.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_sequential_dataset_downloads_itself_and_binary_gets_no_extension()
    {
        var t = await ChosenAsync("MVSCE02.UFSHOME");
        t.Picker.Result = Local("UFSHOME");

        await t.Vm.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "save:UFSHOME" }, t.Picker.Calls);
        Assert.Equal(new byte[] { 0x61, 0x61, 0x00 }, await File.ReadAllBytesAsync(Local("UFSHOME"), TestContext.Current.CancellationToken));
        Assert.Equal($"✓ Downloaded MVSCE02.UFSHOME to {Local("UFSHOME")}.", t.Vm.StatusText);
    }

    [Fact]
    public async Task A_cancelled_save_dialog_downloads_nothing()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");

        await t.Vm.DownloadCommand.ExecuteAsync(null);

        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("readtext:"));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task A_file_dialog_that_cannot_open_is_a_status_line()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");
        t.Picker.Exception = new InvalidOperationException("no dialog");

        await t.Vm.DownloadCommand.ExecuteAsync(null);

        Assert.Equal("✗ Could not open the file dialog: no dialog", t.Vm.StatusText);
    }

    [Fact]
    public async Task Several_members_go_to_a_folder_two_at_a_time()
    {
        var t = await ChosenAsync();
        t.Select("ALLOC", "COMPILE", "HELLO");
        t.Picker.FolderResult = _folder;
        t.Host.Gate = new TaskCompletionSource();

        var download = t.Vm.DownloadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Host.CallsSnapshot().Count(c => c.StartsWith("readtext:")) == 2, "two downloads to start");
        Assert.Equal(new[] { "⟳ Running", "⟳ Running", "⟳ Waiting" }, t.Vm.Members.Select(m => m.Status));
        t.Host.Gate.SetResult();
        await download;

        Assert.Equal(2, t.Host.MaxConcurrent);
        Assert.Equal(new[] { "folder:Download 3 members of MVSCE02.CNTL" }, t.Picker.Calls);
        Assert.All(t.Vm.Members, m => Assert.StartsWith("✓ Done · ", m.Status));
        Assert.Equal(new[] { "ALLOC.txt", "COMPILE.txt", "HELLO.txt" }, Directory.GetFiles(_folder).Select(Path.GetFileName).Order());
        Assert.Equal($"✓ Downloaded 3 of 3 members to {_folder}.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Files_already_there_ask_replace_or_skip_once_for_all()
    {
        var t = await ChosenAsync();
        t.Select("ALLOC", "COMPILE", "HELLO");
        t.Picker.FolderResult = _folder;
        await File.WriteAllTextAsync(Local("ALLOC.txt"), "mine", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Local("COMPILE.txt"), "mine", TestContext.Current.CancellationToken);

        var download = t.Vm.DownloadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null, "the replace question");
        var question = t.Vm.Confirmation!;
        Assert.Equal($"ALLOC.txt already exists in {_folder}.", question.Message);
        Assert.Equal(("Replace", "Skip", true), (question.PrimaryLabel, question.SecondaryLabel, question.OffersApplyToAll));
        question.ApplyToAll = true;
        question.SecondaryCommand.Execute(null);
        await download;

        Assert.Equal(new[] { "– Skipped: the file exists", "– Skipped: the file exists" }, t.Vm.Members.Take(2).Select(m => m.Status));
        Assert.StartsWith("✓ Done", t.Vm.Members[2].Status);
        Assert.Equal("mine", await File.ReadAllTextAsync(Local("ALLOC.txt"), TestContext.Current.CancellationToken));
        Assert.Equal($"⚠ Downloaded 1 of 3 members to {_folder}.", t.Vm.StatusText);
        Assert.False(t.Vm.HasConfirmation);
    }

    [Fact]
    public async Task Replace_overwrites_and_cancel_at_the_question_downloads_nothing()
    {
        var t = await ChosenAsync();
        t.Select("ALLOC", "HELLO");
        t.Picker.FolderResult = _folder;
        await File.WriteAllTextAsync(Local("ALLOC.txt"), "mine", TestContext.Current.CancellationToken);

        var first = t.Vm.DownloadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null, "the replace question");
        t.Vm.Confirmation!.CancelCommand.Execute(null);
        await first;
        Assert.Equal("– Download cancelled.", t.Vm.StatusText);
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("readtext:"));

        var second = t.Vm.DownloadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null, "the replace question again");
        t.Vm.Confirmation!.PrimaryCommand.Execute(null);
        await second;
        Assert.StartsWith("//ALLOC JOB", await File.ReadAllTextAsync(Local("ALLOC.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_failed_member_is_marked_and_the_others_finish()
    {
        var t = await ChosenAsync();
        t.Select("ALLOC", "COMPILE", "HELLO");
        t.Picker.FolderResult = _folder;
        t.Host.Failures["readtext:MVSCE02.CNTL(COMPILE)"] = new HostFileException(HostFileErrorKind.CannotOpen, "x", 3);

        await t.Vm.DownloadCommand.ExecuteAsync(null);

        Assert.Equal("✗ Failed: Not found, not authorized, or cannot be opened.", t.Vm.Members[1].Status);
        Assert.Equal($"⚠ Downloaded 2 of 3 members to {_folder}.", t.Vm.StatusText);
        Assert.False(File.Exists(Local("COMPILE.txt")));
    }

    [Fact]
    public async Task A_connection_failure_stops_the_batch_and_offers_retry()
    {
        const string message = "MVSCE02.CNTL(ALLOC): cannot reach the host (refused).";
        var t = await ChosenAsync();
        t.Select("ALLOC", "COMPILE", "HELLO");
        t.Picker.FolderResult = _folder;
        t.Host.Failures["readtext:MVSCE02.CNTL(ALLOC)"] = new HostFileException(HostFileErrorKind.Unreachable, message);

        await t.Vm.DownloadCommand.ExecuteAsync(null);

        Assert.True(t.Vm.HasError);
        Assert.Equal(message, t.Vm.ErrorText);
        Assert.True(t.Vm.CanRetry);
        Assert.Equal("– Stopped", t.Vm.Members[0].Status);
        Assert.DoesNotContain(t.Vm.Members, m => m.Status.StartsWith("✗"));
        Assert.False(File.Exists(Local("ALLOC.txt")));

        t.Host.Failures.Clear();
        foreach (var file in Directory.GetFiles(_folder)) File.Delete(file);
        await t.Vm.RetryCommand.ExecuteAsync(null);

        Assert.False(t.Vm.HasError);
        Assert.All(t.Vm.Members, m => Assert.StartsWith("✓ Done", m.Status));
        Assert.Equal($"✓ Downloaded 3 of 3 members to {_folder}.", t.Vm.StatusText);
    }

    [Fact]
    public async Task A_single_download_that_cannot_reach_the_host_shows_the_banner()
    {
        const string message = "MVSCE02.CNTL(HELLO): cannot reach the host (refused).";
        var t = await ChosenAsync();
        t.Select("HELLO");
        t.Picker.Result = Local("hello.txt");
        t.Host.Failures["readtext:MVSCE02.CNTL(HELLO)"] = new HostFileException(HostFileErrorKind.Unreachable, message);

        await t.Vm.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(message, t.Vm.ErrorText);
        Assert.True(t.Vm.CanRetry);
        Assert.Equal("– Stopped", t.Vm.Members.Single(m => m.Name == "HELLO").Status);
        Assert.False(File.Exists(Local("hello.txt")));
    }

    [Fact]
    public async Task Cancel_stops_running_and_waiting_downloads()
    {
        var t = await ChosenAsync();
        t.Select("ALLOC", "COMPILE", "HELLO");
        t.Picker.FolderResult = _folder;
        t.Host.Gate = new TaskCompletionSource();

        var download = t.Vm.DownloadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Host.CallsSnapshot().Count(c => c.StartsWith("readtext:")) == 2, "two downloads to start");
        t.Vm.CancelCommand.Execute(null);
        await download;

        Assert.All(t.Vm.Members, m => Assert.Equal("– Cancelled", m.Status));
        Assert.Equal("– Download cancelled.", t.Vm.StatusText);
        Assert.Empty(Directory.GetFiles(_folder));
        Assert.False(t.Vm.IsBusy);
    }

    [Fact]
    public async Task Download_needs_a_member_in_a_pds_but_not_in_a_sequential_dataset()
    {
        var t = await ChosenAsync();
        Assert.False(t.Vm.DownloadCommand.CanExecute(null));
        t.Select("HELLO");
        Assert.True(t.Vm.DownloadCommand.CanExecute(null));

        await t.ChooseAsync("MVSCE02.UFSHOME");
        Assert.True(t.Vm.DownloadCommand.CanExecute(null));
        await t.ChooseAsync("MVSCE02.DB");
        Assert.False(t.Vm.DownloadCommand.CanExecute(null));
    }
}
