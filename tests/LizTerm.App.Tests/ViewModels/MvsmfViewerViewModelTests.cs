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
        Assert.EndsWith("LINE 2000", viewer.Text);
        Assert.DoesNotContain("LINE 2001", viewer.Text);
        Assert.Equal("⚠ Showing the first 2,000 lines of 2,001. Download the member to read it all.",
            viewer.FooterText);
    }

    private static MvsmfViewerViewModel Jcl() =>
        Viewer("//HELLO JOB", "//STEP EXEC PGM=IEFBR14", "//SYSIN DD *", "hello again");

    [Fact]
    public void An_empty_term_finds_nothing_and_says_nothing()
    {
        var viewer = Jcl();

        Assert.Empty(viewer.Matches);
        Assert.Equal(-1, viewer.CurrentIndex);
        Assert.Null(viewer.MatchStart);
        Assert.Equal("", viewer.CountText);
    }

    [Fact]
    public void A_term_finds_every_match_ignoring_case_and_lands_on_the_first()
    {
        var viewer = Jcl();

        viewer.Term = "HELLO";

        Assert.Equal(2, viewer.Matches.Count);
        Assert.Equal(0, viewer.CurrentIndex);
        Assert.Equal(2, viewer.MatchStart);
        Assert.Equal(5, viewer.MatchLength);
        Assert.Equal(0, viewer.MatchLine);
        Assert.Equal("1 of 2", viewer.CountText);
    }

    [Fact]
    public void Next_and_previous_step_through_the_matches_and_wrap()
    {
        var viewer = Jcl();
        viewer.Term = "hello";

        viewer.FindNextCommand.Execute(null);
        Assert.Equal("2 of 2", viewer.CountText);
        Assert.Equal(3, viewer.MatchLine);

        viewer.FindNextCommand.Execute(null);
        Assert.Equal("1 of 2", viewer.CountText);

        viewer.FindPreviousCommand.Execute(null);
        Assert.Equal("2 of 2", viewer.CountText);
    }

    [Fact]
    public void A_term_with_no_match_says_so_and_steps_nowhere()
    {
        var viewer = Jcl();

        viewer.Term = "COBOL";

        Assert.Empty(viewer.Matches);
        Assert.Equal("No matches", viewer.CountText);
        Assert.Null(viewer.MatchStart);
        viewer.FindNextCommand.Execute(null);
        Assert.Equal("No matches", viewer.CountText);
    }

    [Fact]
    public void Clearing_the_term_clears_the_matches()
    {
        var viewer = Jcl();
        viewer.Term = "HELLO";

        viewer.Term = "";

        Assert.Empty(viewer.Matches);
        Assert.Equal("", viewer.CountText);
    }

    [Fact]
    public void Matches_do_not_overlap()
    {
        var viewer = Viewer("AAAA");

        viewer.Term = "AA";

        Assert.Equal(new[] { 0, 2 }, viewer.Matches);
    }

    [Fact]
    public void Find_searches_only_what_is_shown()
    {
        var lines = Enumerable.Range(1, MvsmfViewerViewModel.MaxLines + 1).Select(n => $"LINE {n}").ToArray();
        var viewer = Viewer(lines);

        viewer.Term = $"LINE {MvsmfViewerViewModel.MaxLines + 1}";

        Assert.Equal("No matches", viewer.CountText);
    }

    /// <summary>A changed term must not publish a MatchStart read against the old CurrentIndex and the new Matches
    /// together — the ordering finding: setting Matches first republishes MatchStart at whatever index is still
    /// current, which can be a legal index into the new list that names the wrong match, before CurrentIndex is
    /// reset to the real first one. Here the previous term's second match (index 1) stays a legal index into the
    /// new term's three matches, so the stale read (its second match) is easy to tell from the correct one (its
    /// first).</summary>
    [Fact]
    public void Changing_the_term_never_publishes_a_match_from_the_old_index_into_the_new_list()
    {
        var viewer = Viewer("hello world hello", "world world");
        viewer.Term = "hello";
        viewer.FindNextCommand.Execute(null); // CurrentIndex = 1, the second "hello"

        var published = new List<int?>();
        viewer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MvsmfViewerViewModel.MatchStart)) published.Add(viewer.MatchStart);
        };

        viewer.Term = "world"; // three matches; index 1 is still in range, but names the second, not the first

        var correctFirstMatch = viewer.MatchStart;
        Assert.All(published, value => Assert.True(value is null || value == correctFirstMatch,
            $"Published a stale MatchStart {value} before the correct one ({correctFirstMatch})."));
    }
}
