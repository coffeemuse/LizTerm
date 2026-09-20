// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public class MvsmfBrowserCreateTests
{
    private static async Task<BrowserTestHost> OpenedAsync(string? chosen = "MVSCE02.CNTL")
    {
        var t = BrowserTestHost.Create();
        if (chosen is null) await t.ListAsync();
        else await t.ChooseAsync(chosen);
        t.Vm.NewDatasetCommand.Execute(null);
        Assert.True(t.Vm.IsCreating);
        return t;
    }

    [Fact]
    public async Task New_prefills_from_the_chosen_dataset_and_the_filters_first_qualifier()
    {
        var t = await OpenedAsync();
        var form = t.Vm.Form;

        Assert.Equal(("MVSCE02.", true, "FB", "80", "19040"), (form.Name, form.IsPartitioned, form.Recfm, form.Lrecl, form.Blksize));
        Assert.Equal(("5", "5", "20"), (form.Primary, form.Secondary, form.DirectoryBlocks));
        Assert.Null(form.Message);
        Assert.True(t.Vm.ShowMemberPane);
        Assert.False(t.Vm.CanChooseDataset);
        Assert.False(t.Vm.ShowChooseHint);
        Assert.False(t.Vm.CreateCommand.CanExecute(null));
        Assert.True(t.Vm.CloseFormCommand.CanExecute(null));
    }

    [Fact]
    public async Task New_from_a_load_library_prefills_undefined_length_records()
    {
        var t = await OpenedAsync("MVSCE02.LOAD");

        Assert.Equal(("U", "0", "19069"), (t.Vm.Form.Recfm, t.Vm.Form.Lrecl, t.Vm.Form.Blksize));
    }

    [Fact]
    public async Task New_with_nothing_chosen_keeps_the_defaults_and_names_from_the_filter()
    {
        var t = await OpenedAsync(chosen: null);
        Assert.Equal(("MVSCE02.", true, "FB", "80", "3120"), (t.Vm.Form.Name, t.Vm.Form.IsPartitioned, t.Vm.Form.Recfm, t.Vm.Form.Lrecl, t.Vm.Form.Blksize));

        t.Vm.CloseFormCommand.Execute(null);
        t.Vm.Filter = "**";
        t.Vm.NewDatasetCommand.Execute(null);
        Assert.Equal("", t.Vm.Form.Name);

        t.Vm.CloseFormCommand.Execute(null);
        t.Vm.Filter = " sys1.* ";
        t.Vm.NewDatasetCommand.Execute(null);
        Assert.Equal("SYS1.", t.Vm.Form.Name);
    }

    [Fact]
    public async Task Every_other_operation_is_off_while_the_form_is_open()
    {
        var t = await OpenedAsync();
        t.Select("HELLO");

        Assert.False(t.Vm.NewDatasetCommand.CanExecute(null));
        Assert.False(t.Vm.ListCommand.CanExecute(null));
        Assert.False(t.Vm.DownloadCommand.CanExecute(null));
        Assert.False(t.Vm.UploadCommand.CanExecute(null));
        Assert.False(t.Vm.DeleteCommand.CanExecute(null));
        Assert.False(t.Vm.RenameMemberCommand.CanExecute(null));
        Assert.False(t.Vm.RenameDatasetCommand.CanExecute(null));
        Assert.False(t.Vm.DeleteDatasetCommand.CanExecute(null));

        t.Vm.CloseFormCommand.Execute(null);

        Assert.False(t.Vm.IsCreating);
        Assert.True(t.Vm.ShowMemberPane);
        Assert.True(t.Vm.NewDatasetCommand.CanExecute(null));
        Assert.True(t.Vm.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public async Task Create_sends_the_form_closes_it_and_chooses_the_new_dataset()
    {
        var t = await OpenedAsync();
        t.Vm.Form.Name = "mvsce02.new";
        t.Vm.Form.Primary = "10";
        Assert.True(t.Vm.CreateCommand.CanExecute(null));

        await t.Vm.CreateCommand.ExecuteAsync(null);

        Assert.Contains("create:MVSCE02.NEW", t.Host.CallsSnapshot());
        var created = t.Host.Datasets.Single(d => d.Name == "MVSCE02.NEW");
        Assert.Equal(("PO", "FB", 80, 19040), (created.Attributes!.Dsorg, created.Attributes.Recfm, created.Attributes.Lrecl, created.Attributes.Blksize));
        Assert.False(t.Vm.IsCreating);
        Assert.Equal("MVSCE02.NEW", t.Vm.SelectedDataset?.Name);
        Assert.Contains("members:MVSCE02.NEW", t.Host.CallsSnapshot());
        Assert.Equal("✓ Created MVSCE02.NEW.", t.Vm.StatusText);
    }

    [Fact]
    public async Task The_space_values_are_kept_for_the_next_form()
    {
        var t = await OpenedAsync();
        t.Vm.Form.Name = "MVSCE02.NEW";
        t.Vm.Form.Primary = "10";
        t.Vm.Form.Secondary = "2";
        t.Vm.Form.DirectoryBlocks = "30";
        t.Vm.Form.IsCylinders = true;
        await t.Vm.CreateCommand.ExecuteAsync(null);

        t.Vm.NewDatasetCommand.Execute(null);

        Assert.Equal(("10", "2", "30", true), (t.Vm.Form.Primary, t.Vm.Form.Secondary, t.Vm.Form.DirectoryBlocks, t.Vm.Form.IsCylinders));
        Assert.Equal("MVSCE02.", t.Vm.Form.Name);
    }

    [Fact]
    public async Task Create_outside_the_filter_says_so()
    {
        var t = await OpenedAsync();
        t.Vm.Form.Name = "OTHER.NEW";

        await t.Vm.CreateCommand.ExecuteAsync(null);

        Assert.False(t.Vm.IsCreating);
        Assert.Null(t.Vm.SelectedDataset);
        Assert.Equal("✓ Created OTHER.NEW (not shown by the filter MVSCE02.**).", t.Vm.StatusText);
    }

    [Fact]
    public async Task An_allocation_the_host_refuses_stays_in_the_form()
    {
        var t = await OpenedAsync();
        t.Vm.Form.Name = "MVSCE02.CNTL";

        await t.Vm.CreateCommand.ExecuteAsync(null);

        Assert.True(t.Vm.IsCreating);
        Assert.Equal("✗ The host could not allocate it: it may already exist, there may be no space, or you may not be authorized.", t.Vm.Form.Message);
        Assert.Equal("", t.Vm.StatusText);
        Assert.False(t.Vm.HasError);

        t.Vm.Form.Name = "MVSCE02.NEW";
        await t.Vm.CreateCommand.ExecuteAsync(null);
        Assert.False(t.Vm.IsCreating);
        Assert.Null(t.Vm.Form.Message);
    }

    [Fact]
    public async Task A_request_the_host_rejects_stays_in_the_form_with_its_words()
    {
        var t = await OpenedAsync();
        t.Vm.Form.Name = "MVSCE02.NEW";
        t.Host.Failures["create:MVSCE02.NEW"] = new HostFileException(HostFileErrorKind.InvalidRequest, "x", 3, "Invalid or missing allocation parameters");

        await t.Vm.CreateCommand.ExecuteAsync(null);

        Assert.True(t.Vm.IsCreating);
        Assert.Equal("✗ The host refused the request: Invalid or missing allocation parameters", t.Vm.Form.Message);
    }

    [Fact]
    public async Task A_connection_failure_offers_retry_which_sends_the_form_again()
    {
        var t = await OpenedAsync();
        t.Vm.Form.Name = "MVSCE02.NEW";
        t.Host.Failures["create:MVSCE02.NEW"] = new HostFileException(HostFileErrorKind.Unreachable, "MVSCE02.NEW: cannot reach the host (refused).");

        await t.Vm.CreateCommand.ExecuteAsync(null);
        Assert.True(t.Vm.CanRetry);
        Assert.True(t.Vm.IsCreating);

        t.Host.Failures.Clear();
        t.Vm.Form.Primary = "7";
        await t.Vm.RetryCommand.ExecuteAsync(null);

        Assert.False(t.Vm.HasError);
        Assert.Equal(2, t.Host.CallsSnapshot().Count(c => c == "create:MVSCE02.NEW"));
        Assert.False(t.Vm.IsCreating);
        Assert.Equal("✓ Created MVSCE02.NEW.", t.Vm.StatusText);
    }

    [Fact]
    public async Task A_listing_that_fails_after_a_create_retries_only_the_listing()
    {
        var t = await OpenedAsync();
        t.Vm.Form.Name = "MVSCE02.NEW";
        t.Host.Failures["list:MVSCE02.**"] = new HostFileException(HostFileErrorKind.Unreachable, "cannot reach the host (refused).");

        await t.Vm.CreateCommand.ExecuteAsync(null);
        Assert.True(t.Vm.CanRetry);
        Assert.False(t.Vm.IsCreating);

        t.Host.Failures.Clear();
        await t.Vm.RetryCommand.ExecuteAsync(null);

        Assert.Equal(1, t.Host.CallsSnapshot().Count(c => c == "create:MVSCE02.NEW"));
        Assert.Equal("MVSCE02.NEW", t.Vm.SelectedDataset?.Name);
        Assert.Equal("✓ Created MVSCE02.NEW.", t.Vm.StatusText);
    }

    [Fact]
    public async Task Create_is_off_while_a_field_has_a_problem()
    {
        var t = await OpenedAsync();
        t.Vm.Form.Name = "MVSCE02.NEW";
        var changed = 0;
        t.Vm.CreateCommand.CanExecuteChanged += (_, _) => changed++;

        t.Vm.Form.Lrecl = "x";
        Assert.False(t.Vm.CreateCommand.CanExecute(null));
        Assert.True(changed > 0);

        t.Vm.Form.Lrecl = "80";
        Assert.True(t.Vm.CreateCommand.CanExecute(null));
    }

    [Fact]
    public async Task Closing_the_form_drops_a_failed_creates_retry()
    {
        var t = await OpenedAsync();
        t.Vm.Form.Name = "MVSCE02.NEW";
        t.Host.Failures["create:MVSCE02.NEW"] = new HostFileException(HostFileErrorKind.Unreachable, "MVSCE02.NEW: cannot reach the host (refused).");
        await t.Vm.CreateCommand.ExecuteAsync(null);
        Assert.True(t.Vm.CanRetry);

        t.Vm.CloseFormCommand.Execute(null);

        Assert.False(t.Vm.IsCreating);
        Assert.False(t.Vm.HasError);
        Assert.False(t.Vm.CanRetry);
        t.Host.Failures.Clear();
        await t.Vm.RetryCommand.ExecuteAsync(null);
        Assert.Equal(1, t.Host.CallsSnapshot().Count(c => c == "create:MVSCE02.NEW"));
    }

    [Fact]
    public async Task Create_with_a_filter_the_rules_refuse_says_so()
    {
        var t = await OpenedAsync();
        t.Vm.Form.Name = "MVSCE02.NEW";
        t.Vm.Filter = "";

        await t.Vm.CreateCommand.ExecuteAsync(null);

        Assert.False(t.Vm.IsCreating);
        Assert.Equal(1, t.Host.CallsSnapshot().Count(c => c.StartsWith("list:")));
        Assert.Equal("MVSCE02.CNTL", t.Vm.SelectedDataset?.Name);
        Assert.Equal("⚠ Created MVSCE02.NEW. The list was not refreshed: Enter a dataset filter.", t.Vm.StatusText);
    }
}
