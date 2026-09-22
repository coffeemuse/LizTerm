// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.ViewModels;

public sealed class MvsmfBrowserViewTests
{
    /// <summary>The standard seed plus a sequential dataset whose records are text: the standard one
    /// (MVSCE02.UFSHOME) is RECFM U, which View is off for.</summary>
    private static async Task<BrowserTestHost> ChosenAsync(string dataset = "MVSCE02.CNTL")
    {
        var t = BrowserTestHost.Create(seed: host =>
        {
            BrowserTestHost.Standard(host);
            host.AddDataset("MVSCE02.NOTES", dsorg: "PS", recfm: "FB", lrecl: 80, blksize: 3120);
        });
        t.Host.Text["MVSCE02.CNTL(HELLO)"] = ["//HELLO JOB   ", "//STEP EXEC PGM=IEFBR14"];
        t.Host.Text["MVSCE02.NOTES"] = ["Notes on the batch run."];
        t.Host.Text["MVSCE02.LOAD(PROG)"] = ["not really text"];
        await t.ChooseAsync(dataset);
        return t;
    }

    [Fact]
    public async Task View_needs_exactly_one_member()
    {
        var t = await ChosenAsync();
        Assert.False(t.Vm.ViewCommand.CanExecute(null));

        t.Select("HELLO");
        Assert.True(t.Vm.ViewCommand.CanExecute(null));

        t.Select("HELLO", "ALLOC");
        Assert.False(t.Vm.ViewCommand.CanExecute(null));
    }

    [Fact]
    public async Task Viewing_a_member_reads_it_as_text_and_reports_the_lines()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");

        await t.Vm.ViewCommand.ExecuteAsync(null);

        Assert.Contains("readtext:MVSCE02.CNTL(HELLO)", t.Host.CallsSnapshot());
        Assert.Equal("MVSCE02.CNTL(HELLO)", t.Vm.Viewer!.Path);
        Assert.Equal("//HELLO JOB\n//STEP EXEC PGM=IEFBR14", t.Vm.Viewer.Text);
        Assert.Equal("✓ Read MVSCE02.CNTL(HELLO) · 2 lines.", t.Vm.StatusText);
        Assert.Equal("", t.Vm.Members.Single(m => m.Name == "HELLO").Status);
    }

    [Fact]
    public async Task A_sequential_dataset_views_itself()
    {
        var t = await ChosenAsync("MVSCE02.NOTES");

        Assert.True(t.Vm.ViewCommand.CanExecute(null));
        await t.Vm.ViewCommand.ExecuteAsync(null);

        Assert.Contains("readtext:MVSCE02.NOTES", t.Host.CallsSnapshot());
        Assert.Equal("MVSCE02.NOTES", t.Vm.Viewer!.Path);
        Assert.Equal("✓ Read MVSCE02.NOTES · 1 line.", t.Vm.StatusText);
    }

    [Fact]
    public async Task View_is_off_for_a_dataset_whose_records_are_not_text_and_says_why()
    {
        var t = await ChosenAsync("MVSCE02.LOAD");
        t.Select("PROG");

        Assert.False(t.Vm.ViewCommand.CanExecute(null));
        Assert.Equal("MVSCE02.LOAD holds undefined-length records, which are not text.", t.Vm.ViewHint);
    }

    [Fact]
    public async Task The_hint_names_the_gesture_and_the_kind_of_target()
    {
        var t = await ChosenAsync();
        t.Vm.ViewGestureText = "Ctrl+Enter";
        Assert.Equal("View the selected member (Ctrl+Enter)", t.Vm.ViewHint);

        await t.ChooseAsync("MVSCE02.NOTES");
        Assert.Equal("View this dataset (Ctrl+Enter)", t.Vm.ViewHint);
    }

    [Fact]
    public async Task View_reads_text_even_while_the_transfer_mode_is_binary()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");
        t.Vm.Mode = HostTransferMode.Binary;

        await t.Vm.ViewCommand.ExecuteAsync(null);

        Assert.Contains("readtext:MVSCE02.CNTL(HELLO)", t.Host.CallsSnapshot());
        Assert.DoesNotContain(t.Host.CallsSnapshot(), c => c.StartsWith("readbinary:"));
    }

    [Fact]
    public async Task View_honours_trim_trailing_blanks()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");
        t.Vm.TrimTrailingBlanks = false;

        await t.Vm.ViewCommand.ExecuteAsync(null);

        Assert.StartsWith("//HELLO JOB   \n", t.Vm.Viewer!.Text);
    }

    [Fact]
    public async Task View_asks_for_no_stamp_so_the_memory_is_left_alone()
    {
        var t = await ChosenAsync();
        t.Host.Etags["MVSCE02.CNTL(HELLO)"] = "stamp-1";
        t.Select("HELLO");

        await t.Vm.ViewCommand.ExecuteAsync(null);

        // The request itself, not just the memory: asking costs the host a second pass over the content, and
        // Remember is never called here either way, so the memory alone would pass with withEtag: true.
        Assert.Empty(t.Host.EtagRequests);
        Assert.Null(t.Access.Etags.TryGet(HostPath.ForMember("MVSCE02.CNTL", "HELLO")));
    }

    [Fact]
    public async Task A_connection_failure_banners_with_retry_and_opens_no_viewer()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");
        t.Host.Failures["readtext:MVSCE02.CNTL(HELLO)"] =
            new HostFileException(HostFileErrorKind.Unreachable, "cannot reach the host (refused).");

        await t.Vm.ViewCommand.ExecuteAsync(null);

        Assert.True(t.Vm.HasError);
        Assert.True(t.Vm.CanRetry);
        Assert.Null(t.Vm.Viewer);
    }

    [Fact]
    public async Task A_read_that_fails_for_anything_else_is_the_status_line_and_opens_no_viewer()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");
        t.Host.Failures["readtext:MVSCE02.CNTL(HELLO)"] =
            new HostFileException(HostFileErrorKind.CannotOpen, "x", 3);

        await t.Vm.ViewCommand.ExecuteAsync(null);

        Assert.False(t.Vm.HasError);
        Assert.StartsWith("✗ ", t.Vm.StatusText);
        Assert.Null(t.Vm.Viewer);
    }

    /// <summary>The stale-retry race: a failed View leaves the retry set, and a member filter keystroke on a
    /// fully-loaded library can narrow the selection to nothing (SetSelectedMembers([])) without clearing it.
    /// Retry must not index the now-empty selection; the command is already disabled in that state, so it returns
    /// quietly rather than crashing the status line with a raw ArgumentOutOfRangeException.</summary>
    [Fact]
    public async Task Retry_after_the_selection_narrows_to_nothing_returns_quietly()
    {
        var t = await ChosenAsync();
        t.Select("HELLO");
        t.Host.Failures["readtext:MVSCE02.CNTL(HELLO)"] =
            new HostFileException(HostFileErrorKind.Unreachable, "cannot reach the host (refused).");
        await t.Vm.ViewCommand.ExecuteAsync(null);
        Assert.True(t.Vm.CanRetry);

        t.Select();
        Assert.False(t.Vm.ViewCommand.CanExecute(null));

        await t.Vm.RetryCommand.ExecuteAsync(null);

        Assert.False(t.Vm.HasError);
        Assert.Null(t.Vm.Viewer);
    }
}
