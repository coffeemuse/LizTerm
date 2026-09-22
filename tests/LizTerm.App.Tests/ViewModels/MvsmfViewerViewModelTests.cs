// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;

namespace LizTerm.App.Tests.ViewModels;

public sealed class MvsmfViewerViewModelTests
{
    private static MvsmfViewerViewModel Viewer(params string[] lines) =>
        new("MVSCE02.CNTL(HELLO)", lines, trimTrailingBlanks: true);

    [Fact]
    public void The_title_names_the_path()
    {
        Assert.Equal("MVSCE02.CNTL(HELLO) — mvsMF Access", Viewer("//HELLO JOB").Title);
        Assert.Equal("MVSCE02.CNTL(HELLO)", Viewer("//HELLO JOB").Path);
    }

    [Fact]
    public void The_text_is_the_records_one_to_a_line_with_trailing_blanks_trimmed()
    {
        var viewer = Viewer("//HELLO JOB   ", "//STEP EXEC PGM=IEFBR14");

        Assert.Equal("//HELLO JOB\n//STEP EXEC PGM=IEFBR14", viewer.Text);
        Assert.Equal(2, viewer.LineCount);
        Assert.False(viewer.IsTruncated);
    }

    [Fact]
    public void Trailing_blanks_can_be_kept()
    {
        var viewer = new MvsmfViewerViewModel("MVSCE02.NOTES", ["//HELLO JOB   "], trimTrailingBlanks: false);

        Assert.Equal("//HELLO JOB   ", viewer.Text);
        Assert.Equal("1 line", viewer.FooterText);
    }

    [Fact]
    public void The_footer_counts_the_lines_and_says_what_was_done_to_them()
    {
        Assert.Equal("1 line · trailing blanks trimmed", Viewer("A").FooterText);
        Assert.Equal("2 lines · trailing blanks trimmed", Viewer("A", "B").FooterText);
    }

    [Fact]
    public void An_empty_member_is_no_lines_and_no_numbers()
    {
        var viewer = Viewer();

        Assert.Equal("", viewer.Text);
        Assert.Equal("", viewer.LineNumbers);
        Assert.Equal("0 lines · trailing blanks trimmed", viewer.FooterText);
    }

    [Fact]
    public void The_gutter_numbers_every_line_shown()
    {
        Assert.Equal("1\n2\n3", Viewer("A", "B", "C").LineNumbers);
        Assert.True(Viewer("A").ShowLineNumbers);
    }

    [Fact]
    public void The_gutter_numbers_carry_no_thousands_separator()
    {
        var viewer = Viewer(Enumerable.Range(1, 1001).Select(n => $"LINE {n}").ToArray());

        Assert.Contains("\n1000\n", viewer.LineNumbers);
        Assert.DoesNotContain(",", viewer.LineNumbers);
    }

    [Fact]
    public void Past_the_cap_the_first_lines_are_shown_and_the_footer_says_so()
    {
        var lines = Enumerable.Range(1, MvsmfViewerViewModel.MaxLines + 1).Select(n => $"LINE {n}").ToArray();

        var viewer = Viewer(lines);

        Assert.True(viewer.IsTruncated);
        Assert.Equal(MvsmfViewerViewModel.MaxLines, viewer.LineCount);
        Assert.EndsWith("LINE 10000", viewer.Text);
        Assert.DoesNotContain("LINE 10001", viewer.Text);
        Assert.Equal("⚠ Showing the first 10,000 lines of 10,001. Download the member to read it all.",
            viewer.FooterText);
    }
}
