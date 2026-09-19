// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>Spec §5: a member downloaded or written in this window is replaced only if it has not changed on the
/// host since; anything else gets today's behaviour.</summary>
public sealed class MvsmfBrowserEtagTests : IDisposable
{
    private static readonly HostPath Hello = HostPath.ForMember("MVSCE02.CNTL", "HELLO");
    private readonly string _folder = Directory.CreateTempSubdirectory("lizterm-etag-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Local(string name) => Path.Combine(_folder, name);

    private string Write(string name, string text)
    {
        var path = Local(name);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>CNTL with HELLO's text and a host stamp on it.</summary>
    private static async Task<BrowserTestHost> ChosenAsync()
    {
        var t = BrowserTestHost.Create();
        t.Host.Text["MVSCE02.CNTL(HELLO)"] = ["old"];
        t.Host.Etags["MVSCE02.CNTL(HELLO)"] = "seed-1";
        t.Host.Binary["MVSCE02.UFSHOME"] = [1, 2, 3];
        t.Host.Etags["MVSCE02.UFSHOME"] = "seed-2";
        await t.ChooseAsync("MVSCE02.CNTL");
        return t;
    }

    private async Task<BrowserTestHost> DownloadedAsync()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");
        t.Picker.Result = Local("hello.txt");
        await t.Vm.DownloadCommand.ExecuteAsync(null);
        Assert.Equal("seed-1", t.Access.Etags.TryGet(Hello));
        return t;
    }

    private Task<BrowserTestHost> ReviewingAsync(BrowserTestHost t, string file) => ReviewingAsync(t, [file]);

    private static async Task<BrowserTestHost> ReviewingAsync(BrowserTestHost t, string[] files)
    {
        t.Picker.Results = files;
        await t.Vm.UploadCommand.ExecuteAsync(null);
        return t;
    }

    private static async Task<ConfirmationRequest> AskedAsync(BrowserTestHost t, string message)
    {
        await Wait.UntilAsync(() => t.Vm.Confirmation is { } q && q.Message == message, $"the question {message}");
        return t.Vm.Confirmation!;
    }

    [Fact]
    public async Task A_download_remembers_the_members_stamp()
    {
        var t = await DownloadedAsync();

        Assert.Equal("seed-1", t.Access.Etags.TryGet(Hello));
        Assert.Equal(1, t.Access.Etags.Count);
    }

    [Fact]
    public async Task A_failed_download_remembers_nothing()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");
        t.Picker.Result = Local("hello.txt");
        t.Host.Failures["readtext:MVSCE02.CNTL(HELLO)"] = new HostFileException(HostFileErrorKind.ServerError, "x", 3);

        await t.Vm.DownloadCommand.ExecuteAsync(null);

        Assert.Null(t.Access.Etags.TryGet(Hello));
    }

    [Fact]
    public async Task Replacing_a_downloaded_member_sends_its_stamp_and_keeps_the_new_one()
    {
        var t = await ReviewingAsync(await DownloadedAsync(), Write("hello.jcl", "new\n"));

        var upload = t.Vm.StartUploadCommand.ExecuteAsync(null);
        (await AskedAsync(t, "Member HELLO already exists in MVSCE02.CNTL.")).PrimaryCommand.Execute(null);
        await upload;

        Assert.Equal(new[] { "seed-1" }, t.Host.IfMatches);
        Assert.Equal("✓ Uploaded and verified", t.Vm.Uploads[0].Status);
        Assert.Equal(t.Host.Etags["MVSCE02.CNTL(HELLO)"], t.Access.Etags.TryGet(Hello));
        Assert.StartsWith("stamp-", t.Access.Etags.TryGet(Hello));
    }

    [Fact]
    public async Task A_new_member_sends_no_stamp()
    {
        var t = await ReviewingAsync(await DownloadedAsync(), Write("newmem.jcl", "x\n"));

        await t.Vm.StartUploadCommand.ExecuteAsync(null);

        Assert.Equal(new string?[] { null }, t.Host.IfMatches);
        Assert.Equal(t.Host.Etags["MVSCE02.CNTL(NEWMEM)"], t.Access.Etags.TryGet(HostPath.ForMember("MVSCE02.CNTL", "NEWMEM")));
    }

    [Fact]
    public async Task A_member_never_downloaded_is_replaced_without_a_stamp()
    {
        var t = await ReviewingAsync(await ChosenAsync(), Write("hello.jcl", "new\n"));

        var upload = t.Vm.StartUploadCommand.ExecuteAsync(null);
        (await AskedAsync(t, "Member HELLO already exists in MVSCE02.CNTL.")).PrimaryCommand.Execute(null);
        await upload;

        Assert.Equal(new string?[] { null }, t.Host.IfMatches);
        Assert.Equal(new[] { "new" }, t.Host.Text["MVSCE02.CNTL(HELLO)"]);
    }

    [Fact]
    public async Task A_change_since_the_download_asks_and_replace_anyway_sends_again_without_a_stamp()
    {
        var t = await ReviewingAsync(await DownloadedAsync(), Write("hello.jcl", "new\n"));
        t.Host.Etags["MVSCE02.CNTL(HELLO)"] = "seed-9";

        var upload = t.Vm.StartUploadCommand.ExecuteAsync(null);
        (await AskedAsync(t, "Member HELLO already exists in MVSCE02.CNTL.")).PrimaryCommand.Execute(null);
        var conflict = await AskedAsync(t, "HELLO changed on the host since you downloaded it.");
        Assert.Equal(("Replace anyway", "Skip", false), (conflict.PrimaryLabel, conflict.SecondaryLabel, conflict.OffersApplyToAll));
        conflict.PrimaryCommand.Execute(null);
        await upload;

        Assert.Equal(new string?[] { "seed-1", null }, t.Host.IfMatches);
        Assert.Equal(new[] { "new" }, t.Host.Text["MVSCE02.CNTL(HELLO)"]);
        Assert.Equal("✓ Uploaded and verified", t.Vm.Uploads[0].Status);
        Assert.Equal(t.Host.Etags["MVSCE02.CNTL(HELLO)"], t.Access.Etags.TryGet(Hello));
        Assert.Equal("✓ Uploaded 1 of 1 file to MVSCE02.CNTL.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Skip_leaves_the_changed_member_alone()
    {
        var t = await ReviewingAsync(await DownloadedAsync(), [Write("hello.jcl", "new\n"), Write("other.jcl", "y\n")]);
        t.Host.Etags["MVSCE02.CNTL(HELLO)"] = "seed-9";

        var upload = t.Vm.StartUploadCommand.ExecuteAsync(null);
        (await AskedAsync(t, "Member HELLO already exists in MVSCE02.CNTL.")).PrimaryCommand.Execute(null);
        (await AskedAsync(t, "HELLO changed on the host since you downloaded it.")).SecondaryCommand.Execute(null);
        await upload;

        Assert.Equal("– Skipped: changed on the host", t.Vm.Uploads[0].Status);
        Assert.Equal(new[] { "old" }, t.Host.Text["MVSCE02.CNTL(HELLO)"]);
        Assert.Equal("seed-1", t.Access.Etags.TryGet(Hello));
        Assert.Equal("✓ Uploaded and verified", t.Vm.Uploads[1].Status);
        Assert.Equal("⚠ Uploaded 1 of 2 files to MVSCE02.CNTL.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Cancel_on_the_conflict_stops_the_batch()
    {
        var t = await ReviewingAsync(await DownloadedAsync(), [Write("hello.jcl", "new\n"), Write("other.jcl", "y\n")]);
        t.Host.Etags["MVSCE02.CNTL(HELLO)"] = "seed-9";

        var upload = t.Vm.StartUploadCommand.ExecuteAsync(null);
        (await AskedAsync(t, "Member HELLO already exists in MVSCE02.CNTL.")).PrimaryCommand.Execute(null);
        (await AskedAsync(t, "HELLO changed on the host since you downloaded it.")).CancelCommand.Execute(null);
        await upload;

        Assert.Equal("– Cancelled", t.Vm.Uploads[0].Status);
        Assert.Equal("– Cancelled", t.Vm.Uploads[1].Status);
        Assert.DoesNotContain("writetext:MVSCE02.CNTL(OTHER):1", t.Host.CallsSnapshot());
        Assert.Equal("– Upload cancelled.", t.Vm.StatusText);
        Assert.True(t.Vm.UploadFinished);
    }

    [Fact]
    public async Task A_sequential_dataset_is_checked_the_same_way()
    {
        var t = await ChosenAsync();
        await t.ChooseAsync("MVSCE02.UFSHOME");
        t.Picker.Result = Local("UFSHOME");
        await t.Vm.DownloadCommand.ExecuteAsync(null);
        Assert.Equal("seed-2", t.Access.Etags.TryGet(HostPath.ForDataset("MVSCE02.UFSHOME")));
        t.Host.Etags["MVSCE02.UFSHOME"] = "seed-9";
        t.Picker.Result = Write("new.bin", "abc");

        var upload = t.Vm.UploadCommand.ExecuteAsync(null);
        (await AskedAsync(t, "Replace the contents of MVSCE02.UFSHOME with new.bin?")).PrimaryCommand.Execute(null);
        var conflict = await AskedAsync(t, "MVSCE02.UFSHOME changed on the host since you downloaded it.");
        Assert.Equal(("Replace anyway", false), (conflict.PrimaryLabel, conflict.HasSecondary));
        conflict.PrimaryCommand.Execute(null);
        await upload;

        Assert.Equal(new string?[] { "seed-2", null }, t.Host.IfMatches);
        Assert.Equal("✓ Uploaded new.bin to MVSCE02.UFSHOME.", t.Vm.StatusText);
        Assert.Equal(t.Host.Etags["MVSCE02.UFSHOME"], t.Access.Etags.TryGet(HostPath.ForDataset("MVSCE02.UFSHOME")));
    }

    [Fact]
    public async Task Cancel_on_a_sequential_conflict_sends_nothing_more()
    {
        var t = await ChosenAsync();
        await t.ChooseAsync("MVSCE02.UFSHOME");
        t.Picker.Result = Local("UFSHOME");
        await t.Vm.DownloadCommand.ExecuteAsync(null);
        t.Host.Etags["MVSCE02.UFSHOME"] = "seed-9";
        t.Picker.Result = Write("new.bin", "abc");

        var upload = t.Vm.UploadCommand.ExecuteAsync(null);
        (await AskedAsync(t, "Replace the contents of MVSCE02.UFSHOME with new.bin?")).PrimaryCommand.Execute(null);
        (await AskedAsync(t, "MVSCE02.UFSHOME changed on the host since you downloaded it.")).CancelCommand.Execute(null);
        await upload;

        Assert.Equal(new string?[] { "seed-2" }, t.Host.IfMatches);
        Assert.Equal(new byte[] { 1, 2, 3 }, t.Host.Binary["MVSCE02.UFSHOME"]);
        Assert.Equal("– Upload cancelled.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Deleting_a_member_forgets_its_stamp()
    {
        var t = await DownloadedAsync();
        t.Select("HELLO");

        var deleting = t.Vm.DeleteCommand.ExecuteAsync(null);
        (await AskedAsync(t, "Delete HELLO from MVSCE02.CNTL? This cannot be undone.")).PrimaryCommand.Execute(null);
        await deleting;

        Assert.Null(t.Access.Etags.TryGet(Hello));
    }

    [Fact]
    public async Task A_delete_that_fails_keeps_the_stamp()
    {
        var t = await DownloadedAsync();
        t.Select("HELLO");
        t.Host.Failures["delete:MVSCE02.CNTL(HELLO)"] = new HostFileException(HostFileErrorKind.NotAuthorized, "x", 6);

        var deleting = t.Vm.DeleteCommand.ExecuteAsync(null);
        (await AskedAsync(t, "Delete HELLO from MVSCE02.CNTL? This cannot be undone.")).PrimaryCommand.Execute(null);
        await deleting;

        Assert.Equal("seed-1", t.Access.Etags.TryGet(Hello));
    }
}
