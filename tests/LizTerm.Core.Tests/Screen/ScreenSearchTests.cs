// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Screen;

namespace LizTerm.Core.Tests.Screen;

public class ScreenSearchTests
{
    private static ScreenSnapshot Screen(params string[] rows)
    {
        var buffer = new ScreenBuffer(rows.Length, 40);
        for (var r = 0; r < rows.Length; r++)
            buffer.SetText(r, 0, rows[r], null, null, null);
        return buffer.Snapshot();
    }

    [Fact]
    public void It_finds_a_term_in_reading_order()
    {
        var screen = Screen("READY", "the ready prompt");

        var matches = ScreenSearch.Find(screen, "ready");

        Assert.Equal(
            [ScreenRegion.FromCorners(0, 0, 0, 4), ScreenRegion.FromCorners(1, 4, 1, 8)],
            matches);
    }

    [Fact]
    public void It_is_case_insensitive()
    {
        Assert.Single(ScreenSearch.Find(Screen("SYS1.PROCLIB"), "proclib"));
    }

    [Fact]
    public void It_finds_every_occurrence_on_one_row()
    {
        var matches = ScreenSearch.Find(Screen("ab ab ab"), "ab");

        Assert.Equal([0, 3, 6], matches.Select(m => m.Left));
        Assert.All(matches, m => Assert.Equal(0, m.Top));
    }

    /// <summary>Non-overlapping: the scan resumes after the end of each match, so "aaaa" holds two "aa", not
    /// three. Overlapping hits would double-count the match counter for no gain a user could use.</summary>
    [Fact]
    public void Matches_do_not_overlap()
    {
        Assert.Equal([0, 2], ScreenSearch.Find(Screen("aaaa"), "aa").Select(m => m.Left));
    }

    /// <summary>A 3270 screen is a grid of independent rows, not reflowed text. A term spanning a row edge is
    /// almost always two unrelated fields that happen to abut.</summary>
    [Fact]
    public void A_term_does_not_match_across_a_row_boundary()
    {
        var buffer = new ScreenBuffer(2, 4);
        buffer.SetText(0, 0, "ab", null, null, null);
        buffer.SetText(1, 0, "cd", null, null, null);

        Assert.Empty(ScreenSearch.Find(buffer.Snapshot(), "bc"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_term_matches_nothing(string term)
    {
        Assert.Empty(ScreenSearch.Find(Screen("READY"), term));
    }

    [Fact]
    public void A_term_longer_than_the_row_matches_nothing()
    {
        var buffer = new ScreenBuffer(1, 3);
        buffer.SetText(0, 0, "abc", null, null, null);

        Assert.Empty(ScreenSearch.Find(buffer.Snapshot(), "abcd"));
    }

    /// <summary>A DBCS character occupies two cells, the second carrying RightHalf. The search folds a row into
    /// one entry per character, so a match reports the full span of the characters it covers and can never
    /// begin or end on half of one.</summary>
    [Fact]
    public void A_match_spans_whole_wide_characters()
    {
        var buffer = new ScreenBuffer(1, 6);
        buffer.SetText(0, 0, "a", null, null, null);
        buffer.SetText(0, 1, "中", null, null, CellRendition.Wide | CellRendition.LeftHalf);
        buffer.SetText(0, 2, "中", null, null, CellRendition.Wide | CellRendition.RightHalf);
        buffer.SetText(0, 3, "b", null, null, null);

        var matches = ScreenSearch.Find(buffer.Snapshot(), "a中b");

        var match = Assert.Single(matches);
        Assert.Equal(ScreenRegion.FromCorners(0, 0, 0, 3), match);
    }

    /// <summary>The right half alone is not a character, so a term cannot begin on it.</summary>
    [Fact]
    public void A_match_cannot_begin_on_the_right_half_of_a_wide_character()
    {
        var buffer = new ScreenBuffer(1, 4);
        buffer.SetText(0, 0, "中", null, null, CellRendition.Wide | CellRendition.LeftHalf);
        buffer.SetText(0, 1, "中", null, null, CellRendition.Wide | CellRendition.RightHalf);
        buffer.SetText(0, 2, "x", null, null, null);

        var match = Assert.Single(ScreenSearch.Find(buffer.Snapshot(), "中x"));
        Assert.Equal(0, match.Left);
        Assert.Equal(2, match.Right);
    }
}
