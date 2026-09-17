// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.App.Files;
using LizTerm.App.HostFiles;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.HostFiles;
using LizTerm.Core.Security;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class MvsmfBrowserViewModelTests
{
    [Fact]
    public void The_title_and_filter_come_from_the_profile()
    {
        var t = BrowserTestHost.Create();
        Assert.Equal("mvsMF Browser — MVS/CE (Preview)", t.Vm.Title);
        Assert.Equal("MVSCE02.**", t.Vm.Filter);
        Assert.Equal("", BrowserTestHost.Create(userid: null).Vm.Filter);
        Assert.True(t.Vm.ShowChooseHint);
        Assert.Equal("Choose a dataset on the left.", t.Vm.ChooseHint);
    }

    [Fact]
    public async Task Listing_fills_the_datasets_and_marks_what_cannot_be_opened()
    {
        var t = BrowserTestHost.Create();
        t.Vm.Filter = " mvsce02.** ";

        await t.ListAsync();

        Assert.Equal(new[] { "MVSCE02.CNTL", "MVSCE02.LOAD", "MVSCE02.UFSHOME", "MVSCE02.DB (not supported)" }, t.Vm.Datasets.Select(d => d.DisplayName));
        Assert.Equal("4 datasets", t.Vm.StatusText);
        Assert.Equal(new[] { "list:MVSCE02.**" }, t.Host.CallsSnapshot());
        var cntl = t.Vm.Datasets[0];
        Assert.Equal(("PO", "FB", "80"), (cntl.Dsorg, cntl.Recfm, cntl.Lrecl));
        Assert.False(t.Vm.IsBusy);
    }

    [Fact]
    public async Task A_bad_filter_is_refused_before_asking_the_host()
    {
        var t = BrowserTestHost.Create();
        t.Vm.Filter = "MVSCE02.A B";

        await t.ListAsync();

        Assert.Equal("✗ A filter cannot contain ' '.", t.Vm.StatusText);
        Assert.Empty(t.Host.CallsSnapshot());
    }

    [Fact]
    public async Task Choosing_a_pds_lists_its_members()
    {
        var t = BrowserTestHost.Create();

        await t.ChooseAsync("MVSCE02.CNTL");

        Assert.Equal(new[] { "ALLOC", "COMPILE", "HELLO" }, t.Vm.Members.Select(m => m.Name));
        Assert.Equal(new[] { "ALLOC", "COMPILE", "HELLO" }, t.Vm.VisibleMembers.Select(m => m.Name));
        Assert.Equal("MVSCE02.CNTL · 3 members", t.Vm.MembersHeader);
        Assert.Equal("MVSCE02.CNTL · 3 members", t.Vm.StatusText);
        Assert.True(t.Vm.ShowMembers);
        Assert.False(t.Vm.ShowSequentialNote);
        Assert.False(t.Vm.ShowChooseHint);
        Assert.True(t.Vm.IsTextMode);
        Assert.Equal(HostPath.ForMember("MVSCE02.CNTL", "HELLO"), t.Vm.Members[2].Path);
    }

    [Fact]
    public async Task A_load_library_preselects_binary_and_a_text_library_text()
    {
        var t = BrowserTestHost.Create();

        await t.ChooseAsync("MVSCE02.LOAD");
        Assert.True(t.Vm.IsBinaryMode);
        Assert.False(t.Vm.ShowPaddingNote);

        await t.ChooseAsync("MVSCE02.CNTL");
        Assert.True(t.Vm.IsTextMode);
        t.Vm.IsBinaryMode = true;
        Assert.Equal(HostTransferMode.Binary, t.Vm.Mode);
        Assert.True(t.Vm.ShowPaddingNote);
    }

    [Fact]
    public async Task A_sequential_dataset_shows_its_note_and_lists_no_members()
    {
        var t = BrowserTestHost.Create();

        await t.ChooseAsync("MVSCE02.UFSHOME");

        Assert.True(t.Vm.ShowSequentialNote);
        Assert.False(t.Vm.ShowMembers);
        Assert.Empty(t.Vm.Members);
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("members:"));
    }

    [Fact]
    public async Task A_dataset_the_preview_cannot_open_says_so()
    {
        var t = BrowserTestHost.Create();

        await t.ChooseAsync("MVSCE02.DB");

        Assert.True(t.Vm.ShowChooseHint);
        Assert.Equal("MVSCE02.DB cannot be opened in this release (DSORG DA).", t.Vm.ChooseHint);
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("members:"));
    }

    [Fact]
    public async Task The_member_filter_narrows_the_visible_list()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");

        t.Vm.MemberFilter = "co";

        Assert.Equal(new[] { "COMPILE" }, t.Vm.VisibleMembers.Select(m => m.Name));
        t.Vm.MemberFilter = "";
        Assert.Equal(3, t.Vm.VisibleMembers.Count);
    }

    [Fact]
    public async Task A_connection_failure_shows_the_banner_and_retry_runs_the_listing_again()
    {
        var t = BrowserTestHost.Create();
        t.Host.Failures["list:MVSCE02.**"] = new HostFileException(HostFileErrorKind.Unreachable, "Dataset list: cannot reach the host (refused).");

        await t.ListAsync();

        Assert.True(t.Vm.HasError);
        Assert.Equal("Dataset list: cannot reach the host (refused).", t.Vm.ErrorText);
        Assert.True(t.Vm.CanRetry);
        t.Host.Failures.Clear();
        await t.Vm.RetryCommand.ExecuteAsync(null);
        Assert.False(t.Vm.HasError);
        Assert.Equal(4, t.Vm.Datasets.Count);
    }

    [Fact]
    public async Task Other_failures_go_to_the_status_line()
    {
        var t = BrowserTestHost.Create();
        t.Host.Failures["members:MVSCE02.CNTL"] = new HostFileException(HostFileErrorKind.CannotOpen, "x", 3);

        await t.ChooseAsync("MVSCE02.CNTL");

        Assert.False(t.Vm.HasError);
        Assert.Equal("✗ Not found, not authorized, or cannot be opened.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Busy_blocks_another_listing_and_cancel_ends_it()
    {
        var t = BrowserTestHost.Create();
        t.Host.Gate = new TaskCompletionSource();

        var listing = t.Vm.ListCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Host.CallsSnapshot().Length == 1, "the listing to start");
        Assert.True(t.Vm.IsBusy);
        Assert.False(t.Vm.IsIdle);
        Assert.False(t.Vm.ListCommand.CanExecute(null));
        Assert.True(t.Vm.CancelCommand.CanExecute(null));

        t.Vm.CancelCommand.Execute(null);
        await listing;

        Assert.Equal("– Cancelled.", t.Vm.StatusText);
        Assert.False(t.Vm.IsBusy);
        Assert.True(t.Vm.ListCommand.CanExecute(null));
    }

    [Fact]
    public async Task Selecting_members_is_remembered_and_changing_dataset_clears_it()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");

        t.Select("ALLOC", "HELLO");
        Assert.Equal(new[] { "ALLOC", "HELLO" }, t.Vm.SelectedMembers.Select(m => m.Name));

        await t.ChooseAsync("MVSCE02.LOAD");
        Assert.Empty(t.Vm.SelectedMembers);
    }

    [Fact]
    public async Task The_guide_link_opens_the_user_guide()
    {
        var t = BrowserTestHost.Create();
        await t.Vm.OpenGuideCommand.ExecuteAsync(null);
        Assert.Equal(1, t.GuideOpened);
    }

    [Fact]
    public async Task A_confirmation_answers_its_awaiter()
    {
        var request = new ConfirmationRequest("Replace?", "Replace", "Skip", offersApplyToAll: true);
        Assert.True(request.HasSecondary);
        request.ApplyToAll = true;

        request.SecondaryCommand.Execute(null);

        Assert.Equal(new ConfirmOutcome(ConfirmChoice.Secondary, true), await request.Answer);
    }

    [Fact]
    public void Dispose_cancels_and_releases_the_connection()
    {
        var t = BrowserTestHost.Create();
        t.Vm.Dispose();
        Assert.True(t.Host.Disposed);
    }

    [Fact]
    public async Task A_pin_that_cannot_be_saved_is_a_warning_after_the_operation()
    {
        var presented = new PresentedCertificate("11:22", "CN=proxy", "pem", true, null);
        var trusted = new FakeHostFileService();
        BrowserTestHost.Standard(trusted);
        var refusing = new FakeHostFileService();
        refusing.Failures["list:MVSCE02.**"] = new HostFileException(HostFileErrorKind.CertificateRejected, "x", certificate: presented);
        var access = new HostFileAccess(
            new SessionProfile { Name = "MVS/CE", Host = "mvs", HostFilesUrl = "https://mvs/zosmf", HostFilesUserid = "MVSCE02" },
            (_, pin, _) => pin is null ? refusing : trusted, _ => throw new UnauthorizedAccessException("read-only"));
        var certificates = new FakeCertificatePrompt { Decision = new CertificateDecision(true, true) };
        var vm = new MvsmfBrowserViewModel(access, access.Connect(new FakeCredentialPrompt(), certificates), new FakeFilePicker(), a => a());

        await vm.ListCommand.ExecuteAsync(null);

        Assert.Equal(4, vm.Datasets.Count);
        Assert.Equal("⚠ Could not save the certificate to the profile: read-only", vm.StatusText);
        Assert.False(vm.HasError);

        vm.Dispose();
        vm.StatusText = "";
        access.AcceptPin(new CertificatePin("33:44", "CN=other", "pem"), remember: true);
        Assert.Equal("", vm.StatusText);
    }

    private sealed class DisposingPicker(Action whilePicking, string file) : IFilePicker
    {
        public Task<string?> PickFileToSendAsync()
        {
            whilePicking();
            return Task.FromResult<string?>(file);
        }

        public Task<IReadOnlyList<string>> PickFilesToSendAsync(string title) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveLocationAsync(string suggestedFileName, string title, IReadOnlyList<SaveFormat>? formats = null) =>
            Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task A_closed_browser_asks_nothing_and_takes_the_answer_as_cancel()
    {
        var t = BrowserTestHost.Create();
        MvsmfBrowserViewModel? vm = null;
        vm = new MvsmfBrowserViewModel(t.Access, t.Access.Connect(t.Credentials, t.Certificates),
            new DisposingPicker(() => vm!.Dispose(), "/tmp/lizterm-never-read.bin"), a => a());
        await vm.ListCommand.ExecuteAsync(null);
        vm.SelectedDataset = vm.Datasets.Single(d => d.Name == "MVSCE02.UFSHOME");
        var asked = false;
        vm.PropertyChanged += (_, e) => asked |= e.PropertyName == nameof(vm.HasConfirmation);

        await vm.UploadCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(asked);
        Assert.Null(vm.Confirmation);
        Assert.Equal("– Upload cancelled.", vm.StatusText);
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("write"));
    }
}
