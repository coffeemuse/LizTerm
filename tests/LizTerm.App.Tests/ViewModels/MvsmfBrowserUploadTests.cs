// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public sealed class MvsmfBrowserUploadTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("lizterm-upload-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Write(string name, string text)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    private async Task<BrowserTestHost> ReviewAsync(params string[] files)
    {
        // The standard CNTL already holds HELLO; these tests upload hello.jcl as a new member.
        var t = BrowserTestHost.Create(seed: host =>
        {
            BrowserTestHost.Standard(host);
            host.Members["MVSCE02.CNTL"].Remove("HELLO");
        });
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Picker.Results = files;
        await t.Vm.UploadCommand.ExecuteAsync(null);
        return t;
    }

    private static async Task StartAsync(BrowserTestHost t) => await t.Vm.StartUploadCommand.ExecuteAsync(null);

    [Fact]
    public async Task Choosing_files_opens_the_review_with_names_and_checks()
    {
        var t = await ReviewAsync(
            Write("hello.jcl", "//HELLO JOB\n"),
            Write("long.jcl", new string('X', 81) + "\n"),
            Write("tabs.jcl", "A\tB\n"),
            Write("bad-name.txt", "x\n"));

        Assert.True(t.Vm.IsReviewingUpload);
        Assert.Equal("Upload to MVSCE02.CNTL", t.Vm.UploadHeader);
        Assert.Equal(new[] { "open-many:Upload to MVSCE02.CNTL" }, t.Picker.Calls);
        Assert.Equal(new[] { "HELLO", "LONG", "TABS", "BAD-NAME" }, t.Vm.Uploads.Select(u => u.MemberName));
        Assert.Equal("", t.Vm.Uploads[0].Problems);
        Assert.Equal("✗ Line 1 is 81 characters; the limit is 80.", t.Vm.Uploads[1].Problems);
        Assert.True(t.Vm.Uploads[1].IsBlocked);
        Assert.Equal("⚠ 1 line contains tab characters.", t.Vm.Uploads[2].Problems);
        Assert.False(t.Vm.Uploads[2].IsBlocked);
        Assert.Equal("✗ A member name cannot contain '-'.", t.Vm.Uploads[3].NameProblem);
        Assert.False(t.Vm.UploadCommand.CanExecute(null));
        Assert.True(t.Vm.StartUploadCommand.CanExecute(null));
    }

    [Fact]
    public async Task Bad_names_stop_the_upload_until_fixed_and_blocked_files_are_not_sent()
    {
        var t = await ReviewAsync(
            Write("hello.jcl", "//HELLO JOB\n\n//END\n"),
            Write("long.jcl", new string('X', 81)),
            Write("bad-name.txt", "x\n"));

        await StartAsync(t);
        Assert.Equal("✗ Fix the member names marked ✗ first.", t.Vm.ReviewMessage);
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("write"));

        t.Vm.Uploads[2].MemberName = "badname";
        await StartAsync(t);

        Assert.Null(t.Vm.ReviewMessage);
        Assert.Equal(new[] { "✓ Uploaded and verified", "– Not sent", "✓ Uploaded and verified" }, t.Vm.Uploads.Select(u => u.Status));
        Assert.Equal(new[] { "//HELLO JOB", "", "//END" }, t.Host.Text["MVSCE02.CNTL(HELLO)"]);
        Assert.Contains("writetext:MVSCE02.CNTL(BADNAME):1", t.Host.CallsSnapshot());
        Assert.Equal("⚠ Uploaded 2 of 3 files to MVSCE02.CNTL.", t.Vm.StatusText);
        Assert.True(t.Vm.UploadFinished);
        Assert.False(t.Vm.StartUploadCommand.CanExecute(null));
        Assert.Contains(t.Vm.Members, m => m.Name == "BADNAME");
    }

    [Fact]
    public async Task Two_files_for_one_member_are_refused()
    {
        var t = await ReviewAsync(Write("a.jcl", "x\n"), Write("a.txt", "y\n"));

        await StartAsync(t);

        Assert.Equal("✗ Two files would become member A.", t.Vm.ReviewMessage);
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("write"));
    }

    [Fact]
    public async Task An_existing_member_asks_replace_or_skip()
    {
        var t = await ReviewAsync(Write("alloc.jcl", "new\n"), Write("compile.jcl", "new\n"));
        t.Host.Text["MVSCE02.CNTL(ALLOC)"] = ["old"];

        var upload = t.Vm.StartUploadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null, "the replace question");
        var question = t.Vm.Confirmation!;
        Assert.Equal("Member ALLOC already exists in MVSCE02.CNTL.", question.Message);
        Assert.Equal(("Replace", "Skip", true), (question.PrimaryLabel, question.SecondaryLabel, question.OffersApplyToAll));
        question.SecondaryCommand.Execute(null);
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null && !ReferenceEquals(t.Vm.Confirmation, question), "the second question");
        t.Vm.Confirmation!.PrimaryCommand.Execute(null);
        await upload;

        Assert.Equal("– Skipped: the member exists", t.Vm.Uploads[0].Status);
        Assert.Equal(new[] { "old" }, t.Host.Text["MVSCE02.CNTL(ALLOC)"]);
        Assert.Equal("✓ Uploaded and verified", t.Vm.Uploads[1].Status);
    }

    [Fact]
    public async Task Tabs_are_expanded_unless_turned_off()
    {
        var t = await ReviewAsync(Write("tabs.jcl", "A\tB\n"));
        t.Vm.ExpandTabs = false;
        Assert.Equal(new[] { "A\tB" }, t.Vm.Uploads[0].Check!.Lines);
        t.Vm.ExpandTabs = true;

        await StartAsync(t);

        Assert.Equal(new[] { "A       B" }, t.Host.Text["MVSCE02.CNTL(TABS)"]);
    }

    [Fact]
    public async Task Without_verification_nothing_is_read_back()
    {
        var t = await ReviewAsync(Write("hello.jcl", "x\n"));
        t.Vm.VerifyUploads = false;

        await StartAsync(t);

        Assert.Equal("✓ Uploaded", t.Vm.Uploads[0].Status);
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("readtext:"));
        Assert.Equal("✓ Uploaded 1 of 1 file to MVSCE02.CNTL.", t.Vm.StatusText);
    }

    [Fact]
    public async Task A_failure_mid_write_warns_about_a_partial_member()
    {
        var t = await ReviewAsync(Write("hello.jcl", "x\n"));
        t.Host.Failures["writetext:MVSCE02.CNTL(HELLO)"] = new HostFileException(HostFileErrorKind.ServerError, "x", 3);

        await StartAsync(t);

        Assert.Equal("✗ Failed: Server error (reason 3). The member may be partly written.", t.Vm.Uploads[0].Status);
    }

    [Fact]
    public async Task Binary_uploads_send_the_bytes_unchecked()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.LOAD");
        var file = Path.Combine(_folder, "prog2.bin");
        await File.WriteAllBytesAsync(file, [1, 2, 3], TestContext.Current.CancellationToken);
        t.Picker.Results = [file];
        await t.Vm.UploadCommand.ExecuteAsync(null);
        Assert.Null(t.Vm.Uploads[0].Check);

        await StartAsync(t);

        Assert.Equal(new byte[] { 1, 2, 3 }, t.Host.Binary["MVSCE02.LOAD(PROG2)"]);
        Assert.Equal("✓ Uploaded", t.Vm.Uploads[0].Status);
    }

    [Fact]
    public async Task Cancel_stops_the_file_being_sent_and_the_rest()
    {
        var t = await ReviewAsync(Write("one.jcl", "x\n"), Write("two.jcl", "y\n"));
        // Start lists the members first; let that through, then hold the writes.
        var listing = new TaskCompletionSource();
        t.Host.Gate = listing;

        var upload = t.Vm.StartUploadCommand.ExecuteAsync(null);
        Assert.Equal(2, t.Host.CallsSnapshot().Count(c => c == "members:MVSCE02.CNTL"));
        t.Host.Gate = new TaskCompletionSource();
        listing.SetResult();
        await Wait.UntilAsync(() => t.Host.CallsSnapshot().Any(c => c.StartsWith("writetext:")), "the first upload");
        t.Vm.CancelCommand.Execute(null);
        await upload;

        Assert.Equal(new[] { "– Cancelled", "– Cancelled" }, t.Vm.Uploads.Select(u => u.Status));
        Assert.Equal("– Upload cancelled.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Closing_the_review_clears_it()
    {
        var t = await ReviewAsync(Write("hello.jcl", "x\n"));

        t.Vm.CloseReviewCommand.Execute(null);

        Assert.False(t.Vm.IsReviewingUpload);
        Assert.Empty(t.Vm.Uploads);
        Assert.True(t.Vm.UploadCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_sequential_dataset_asks_before_its_contents_are_replaced()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.UFSHOME");
        var file = Path.Combine(_folder, "data.bin");
        await File.WriteAllBytesAsync(file, [9], TestContext.Current.CancellationToken);
        t.Picker.Result = file;

        var declined = t.Vm.UploadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null, "the replace question");
        Assert.Equal("Replace the contents of MVSCE02.UFSHOME with data.bin?", t.Vm.Confirmation!.Message);
        Assert.Equal("Replace", t.Vm.Confirmation.PrimaryLabel);
        Assert.False(t.Vm.Confirmation.HasSecondary);
        t.Vm.Confirmation.CancelCommand.Execute(null);
        await declined;
        Assert.Equal("– Upload cancelled.", t.Vm.StatusText);

        var accepted = t.Vm.UploadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null, "the replace question again");
        t.Vm.Confirmation!.PrimaryCommand.Execute(null);
        await accepted;
        Assert.Equal(new byte[] { 9 }, t.Host.Binary["MVSCE02.UFSHOME"]);
        Assert.Equal($"✓ Uploaded data.bin to MVSCE02.UFSHOME.", t.Vm.StatusText);
        Assert.False(t.Vm.IsReviewingUpload);
    }

    [Fact]
    public async Task A_sequential_text_upload_is_checked_first()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.UFSHOME");
        t.Vm.IsTextMode = true;
        t.Picker.Result = Write("wide.txt", new string('X', 5000));

        var upload = t.Vm.UploadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null, "the replace question");
        t.Vm.Confirmation!.PrimaryCommand.Execute(null);
        await upload;

        Assert.Equal("✗ Not sent: Line 1 is 5000 characters; the limit is 4096.", t.Vm.StatusText);
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("write"));
    }

    [Fact]
    public async Task A_connection_failure_stops_the_upload_and_offers_retry()
    {
        const string message = "MVSCE02.CNTL(HELLO): cannot reach the host (refused).";
        var t = await ReviewAsync(Write("hello.jcl", "x\n"));
        t.Host.Failures["writetext:MVSCE02.CNTL(HELLO)"] = new HostFileException(HostFileErrorKind.Unreachable, message);

        await StartAsync(t);

        Assert.True(t.Vm.HasError);
        Assert.StartsWith(message, t.Vm.ErrorText);
        Assert.EndsWith("The member may be partly written.", t.Vm.ErrorText);
        Assert.True(t.Vm.CanRetry);
        Assert.Equal("– Stopped: the member may be partly written", t.Vm.Uploads[0].Status);
        Assert.False(t.Vm.UploadFinished);
        Assert.True(t.Vm.StartUploadCommand.CanExecute(null));

        t.Host.Failures.Clear();
        await t.Vm.RetryCommand.ExecuteAsync(null);

        Assert.False(t.Vm.HasError);
        Assert.Equal("✓ Uploaded and verified", t.Vm.Uploads[0].Status);
        Assert.Equal(new[] { "x" }, t.Host.Text["MVSCE02.CNTL(HELLO)"]);
    }

    [Fact]
    public async Task A_connection_failure_stops_the_waiting_uploads_too()
    {
        var t = await ReviewAsync(Write("hello.jcl", "x\n"), Write("second.jcl", "y\n"));
        t.Host.Failures["writetext:MVSCE02.CNTL(HELLO)"] = new HostFileException(HostFileErrorKind.Unreachable, "down");

        await StartAsync(t);

        Assert.Equal(new[] { "– Stopped: the member may be partly written", "– Stopped" }, t.Vm.Uploads.Select(u => u.Status));
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("writetext:MVSCE02.CNTL(SECOND)"));
    }

    [Fact]
    public async Task A_sequential_upload_that_cannot_reach_the_host_offers_retry()
    {
        const string message = "MVSCE02.UFSHOME: cannot reach the host (refused).";
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.UFSHOME");
        var file = Path.Combine(_folder, "data.bin");
        await File.WriteAllBytesAsync(file, [9], TestContext.Current.CancellationToken);
        t.Picker.Result = file;
        t.Host.Failures["writebinary:MVSCE02.UFSHOME"] = new HostFileException(HostFileErrorKind.Unreachable, message);

        var upload = t.Vm.UploadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null, "the replace question");
        t.Vm.Confirmation!.PrimaryCommand.Execute(null);
        await upload;

        Assert.Equal(message + " The member may be partly written.", t.Vm.ErrorText);
        Assert.True(t.Vm.CanRetry);

        t.Host.Failures.Clear();
        var retry = t.Vm.RetryCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null, "the replace question again");
        t.Vm.Confirmation!.PrimaryCommand.Execute(null);
        await retry;

        Assert.Equal(new byte[] { 9 }, t.Host.Binary["MVSCE02.UFSHOME"]);
        Assert.Equal("✓ Uploaded data.bin to MVSCE02.UFSHOME.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Start_asks_about_a_member_the_list_did_not_show()
    {
        var t = BrowserTestHost.Create();
        t.Host.Text["MVSCE02.CNTL(ALLOC)"] = ["old"];
        t.Host.Failures["members:MVSCE02.CNTL"] = new HostFileException(HostFileErrorKind.Unreachable, "down");
        await t.ChooseAsync("MVSCE02.CNTL");
        Assert.True(t.Vm.HasError);
        Assert.Empty(t.Vm.Members);
        t.Host.Failures.Clear();
        t.Picker.Results = [Write("alloc.jcl", "new\n")];
        await t.Vm.UploadCommand.ExecuteAsync(null);

        var upload = t.Vm.StartUploadCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.Confirmation is not null, "the replace question");
        Assert.Equal("Member ALLOC already exists in MVSCE02.CNTL.", t.Vm.Confirmation!.Message);
        t.Vm.Confirmation.CancelCommand.Execute(null);
        await upload;

        Assert.Equal(new[] { "old" }, t.Host.Text["MVSCE02.CNTL(ALLOC)"]);
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("write"));
    }

    [Fact]
    public async Task Retry_after_a_stop_sends_only_what_was_not_sent()
    {
        var t = await ReviewAsync(Write("one.jcl", "x\n"), Write("two.jcl", "y\n"));
        t.Host.Failures["writetext:MVSCE02.CNTL(TWO)"] = new HostFileException(HostFileErrorKind.Unreachable, "down");

        await StartAsync(t);
        Assert.Equal(new[] { "✓ Uploaded and verified", "– Stopped: the member may be partly written" },
            t.Vm.Uploads.Select(u => u.Status));
        var writes = t.Host.CallsSnapshot().Count(c => c.StartsWith("writetext:"));

        t.Host.Failures.Clear();
        await t.Vm.RetryCommand.ExecuteAsync(null);

        var after = t.Host.CallsSnapshot().Where(c => c.StartsWith("writetext:")).Skip(writes).ToArray();
        Assert.Equal(new[] { "writetext:MVSCE02.CNTL(TWO):1" }, after);
        Assert.Equal(new[] { "✓ Uploaded and verified", "✓ Uploaded and verified" }, t.Vm.Uploads.Select(u => u.Status));
        Assert.Equal("✓ Uploaded 2 of 2 files to MVSCE02.CNTL.", t.Vm.StatusText);
        Assert.True(t.Vm.UploadFinished);
    }
}
