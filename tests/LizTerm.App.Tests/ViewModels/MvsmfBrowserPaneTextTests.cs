// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>The strings the two panes show (pane-pattern spec §4.1 and §4.2), the drop-down's label (§5), the
/// padding note on the status line, and Refresh.</summary>
public class MvsmfBrowserPaneTextTests
{
    [Fact]
    public void The_title_names_mvsmf_access()
    {
        var t = BrowserTestHost.Create();
        Assert.Equal("mvsMF Access — MVS/CE (Preview)", t.Vm.Title);
    }

    [Fact]
    public async Task The_footers_count_what_is_listed_and_selected()
    {
        var t = BrowserTestHost.Create();
        Assert.Equal("No datasets", t.Vm.DatasetsFooter);
        Assert.Equal("Members", t.Vm.MembersTitle);
        Assert.Equal("", t.Vm.MembersFooter);

        await t.ListAsync();
        Assert.Equal("4 datasets · none selected", t.Vm.DatasetsFooter);

        await t.ChooseAsync("MVSCE02.CNTL");
        Assert.Equal("4 datasets · 1 selected", t.Vm.DatasetsFooter);
        Assert.Equal("MVSCE02.CNTL", t.Vm.MembersTitle);
        Assert.Equal("3 members · none selected", t.Vm.MembersFooter);

        t.Select("ALLOC", "HELLO");
        Assert.Equal("3 members · 2 selected", t.Vm.MembersFooter);

        await t.ChooseAsync("MVSCE02.UFSHOME");
        Assert.Equal("MVSCE02.UFSHOME", t.Vm.MembersTitle);
        Assert.Equal("", t.Vm.MembersFooter);
    }

    [Fact]
    public async Task The_footers_mark_a_list_the_host_has_more_of()
    {
        var t = BrowserTestHost.Create(seed: BrowserTestHost.Large, pageSize: 2);
        await t.ListAsync();
        Assert.Equal("2+ datasets · none selected", t.Vm.DatasetsFooter);

        await t.ChooseAsync("MVSCE02.BIG");
        Assert.Equal("2+ members · none selected", t.Vm.MembersFooter);

        await t.FilterMembersAsync("L");
        Assert.Equal("2+ matching · none selected", t.Vm.MembersFooter);
    }

    [Fact]
    public async Task A_local_member_filter_counts_the_rows_it_shows()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");

        await t.FilterMembersAsync("HEL");

        Assert.Equal("1 of 3 members · none selected", t.Vm.MembersFooter);
    }

    [Fact]
    public async Task An_open_review_titles_the_pane_and_counts_its_files()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        var file = Path.Combine(Path.GetTempPath(), $"lizterm-{Guid.NewGuid():N}.jcl");
        await File.WriteAllTextAsync(file, "x\n", TestContext.Current.CancellationToken);
        try
        {
            t.Picker.Results = [file];
            await t.Vm.UploadCommand.ExecuteAsync(null);

            Assert.True(t.Vm.IsReviewingUpload);
            Assert.Equal("Upload to MVSCE02.CNTL", t.Vm.MembersTitle);
            Assert.Equal("1 file", t.Vm.MembersFooter);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task The_transfer_label_follows_the_mode()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        Assert.Equal("Transfer: Text", t.Vm.TransferModeLabel);

        t.Vm.IsBinaryMode = true;
        Assert.Equal("Transfer: Binary", t.Vm.TransferModeLabel);
    }

    [Fact]
    public async Task Choosing_binary_on_a_fixed_dataset_puts_the_padding_note_on_the_status_line()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        Assert.Equal("3 members", t.Vm.StatusText);

        t.Vm.IsBinaryMode = true;
        Assert.Equal(MvsmfBrowserViewModel.PaddingNote, t.Vm.StatusText);

        // A load library is binary by itself and not fixed-length: the listing's count stays.
        await t.ChooseAsync("MVSCE02.LOAD");
        Assert.True(t.Vm.IsBinaryMode);
        Assert.Equal("1 member", t.Vm.StatusText);
    }

    [Fact]
    public async Task Refresh_lists_the_filter_again_and_keeps_the_chosen_dataset()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Host.AddDataset("MVSCE02.NEW", dsorg: "PS");
        t.Host.Members["MVSCE02.CNTL"].Add("ADDED");

        await t.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(5, t.Vm.Datasets.Count);
        Assert.Equal("MVSCE02.CNTL", t.Vm.SelectedDataset?.Name);
        Assert.Equal(4, t.Vm.Members.Count);
        Assert.Equal("4 members", t.Vm.StatusText);
    }

    [Fact]
    public async Task Refresh_says_when_the_chosen_dataset_is_gone()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        t.Host.Datasets.RemoveAll(d => d.Name == "MVSCE02.CNTL");

        await t.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Null(t.Vm.SelectedDataset);
        Assert.Equal(3, t.Vm.Datasets.Count);
        Assert.Equal("⚠ MVSCE02.CNTL is no longer listed. 3 datasets", t.Vm.StatusText);
    }

    [Fact]
    public async Task Refresh_says_when_the_chosen_dataset_fell_off_the_first_page()
    {
        var t = BrowserTestHost.Create(seed: BrowserTestHost.Large, pageSize: 2);
        await t.ChooseAsync("MVSCE02.UFSHOME");

        await t.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Null(t.Vm.SelectedDataset);
        Assert.True(t.Vm.HasMoreDatasets);
        Assert.Equal(
            "⚠ MVSCE02.UFSHOME is not on the first page of MVSCE02.**; load more datasets or narrow the filter. 2 datasets shown, more on the host",
            t.Vm.StatusText);
    }

    [Fact]
    public async Task Refresh_with_nothing_chosen_is_a_plain_listing()
    {
        var t = BrowserTestHost.Create();
        await t.ListAsync();
        t.Host.AddDataset("MVSCE02.NEW", dsorg: "PS");

        await t.Vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(5, t.Vm.Datasets.Count);
        Assert.Equal("5 datasets", t.Vm.StatusText);
    }

    [Fact]
    public async Task Refresh_is_off_while_an_operation_runs_or_a_review_is_open()
    {
        var t = BrowserTestHost.Create();
        await t.ChooseAsync("MVSCE02.CNTL");
        Assert.True(t.Vm.RefreshCommand.CanExecute(null));

        t.Host.Gate = new TaskCompletionSource();
        var listing = t.Vm.ListCommand.ExecuteAsync(null);
        await Wait.UntilAsync(() => t.Vm.IsBusy, "the listing to start");
        Assert.False(t.Vm.RefreshCommand.CanExecute(null));
        t.Host.Gate.SetResult();
        await listing;
        Assert.True(t.Vm.RefreshCommand.CanExecute(null));
    }
}
