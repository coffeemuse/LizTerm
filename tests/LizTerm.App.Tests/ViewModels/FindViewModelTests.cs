// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.ViewModels;

public class FindViewModelTests
{
    private static ScreenSnapshot Screen(params string[] rows)
    {
        var buffer = new ScreenBuffer(Math.Max(rows.Length, 1), 40);
        for (var r = 0; r < rows.Length; r++)
            buffer.SetText(r, 0, rows[r], null, null, null);
        return buffer.Snapshot();
    }

    private static (FindViewModel Find, List<(int Row, int Column)> Moves) Build(ScreenSnapshot? screen = null)
    {
        var moves = new List<(int, int)>();
        var find = new FindViewModel((row, column) => { moves.Add((row, column)); return Task.CompletedTask; });
        find.OnScreen(screen ?? Screen("ab", "ab", "ab"));
        return (find, moves);
    }

    [Fact]
    public void It_starts_closed_with_no_matches()
    {
        var (find, _) = Build();

        Assert.False(find.IsOpen);
        Assert.Empty(find.Matches);
        Assert.Null(find.CurrentMatch);
    }

    [Fact]
    public void Typing_a_term_finds_matches_and_selects_the_first_without_moving_the_cursor()
    {
        var (find, moves) = Build();
        find.Open();

        find.Term = "ab";

        Assert.Equal(3, find.Matches.Count);
        Assert.Equal(0, find.CurrentIndex);
        Assert.Empty(moves);
        Assert.Equal("1 of 3", find.CountText);
    }

    /// <summary>The first Enter goes to the match already highlighted, rather than skipping past it.</summary>
    [Fact]
    public async Task The_first_next_moves_to_the_first_match()
    {
        var (find, moves) = Build();
        find.Open();
        find.Term = "ab";

        await find.NextAsync();

        Assert.Equal(0, find.CurrentIndex);
        Assert.Equal([(0, 0)], moves);
    }

    [Fact]
    public async Task Next_walks_forward_and_wraps()
    {
        var (find, moves) = Build();
        find.Open();
        find.Term = "ab";

        await find.NextAsync();
        await find.NextAsync();
        await find.NextAsync();
        await find.NextAsync();

        Assert.Equal([(0, 0), (1, 0), (2, 0), (0, 0)], moves);
        Assert.Equal("1 of 3", find.CountText);
    }

    [Fact]
    public async Task Previous_walks_back_and_wraps()
    {
        var (find, moves) = Build();
        find.Open();
        find.Term = "ab";

        await find.NextAsync();
        await find.PreviousAsync();

        Assert.Equal([(0, 0), (2, 0)], moves);
    }

    [Fact]
    public void No_matches_reports_so_and_moves_nothing()
    {
        var (find, moves) = Build();
        find.Open();

        find.Term = "zz";

        Assert.Empty(find.Matches);
        Assert.Equal(-1, find.CurrentIndex);
        Assert.Equal("No matches", find.CountText);
        Assert.Empty(moves);
    }

    [Fact]
    public void A_blank_term_reports_nothing_at_all()
    {
        var (find, _) = Build();
        find.Open();

        find.Term = "";

        Assert.Equal("", find.CountText);
    }

    /// <summary>A 3270 screen repaints on every keystroke echo. Clearing matches the way Selection clears would
    /// make the highlight vanish immediately and read as broken (spec 5.5).</summary>
    [Fact]
    public void A_repaint_recomputes_the_matches()
    {
        var (find, _) = Build();
        find.Open();
        find.Term = "ab";

        find.OnScreen(Screen("ab", "ab", "ab", "ab"));

        Assert.Equal(4, find.Matches.Count);
    }

    /// <summary>Re-anchored by position: a repaint that leaves your match where it was does not move you.</summary>
    [Fact]
    public async Task A_repaint_keeps_the_current_match_when_it_is_still_there()
    {
        var (find, _) = Build();
        find.Open();
        find.Term = "ab";
        await find.NextAsync();
        await find.NextAsync();
        Assert.Equal(1, find.CurrentIndex);

        find.OnScreen(Screen("ab", "ab", "ab", "ab"));

        Assert.Equal(1, find.CurrentIndex);
    }

    /// <summary>And a repaint that rewrites the screen underneath you starts over rather than pointing at
    /// something arbitrary.</summary>
    [Fact]
    public async Task A_repaint_that_removes_the_current_match_resets_to_the_first()
    {
        var (find, _) = Build();
        find.Open();
        find.Term = "ab";
        await find.NextAsync();
        await find.NextAsync();
        Assert.Equal(1, find.CurrentIndex);

        find.OnScreen(Screen("xx", "xx", "ab"));

        Assert.Equal(0, find.CurrentIndex);
        Assert.Single(find.Matches);
    }

    /// <summary>Changing the term is a new search, so the next Enter visits its first match rather than
    /// advancing past it.</summary>
    [Fact]
    public async Task Changing_the_term_starts_the_walk_again()
    {
        var (find, moves) = Build(Screen("ab cd", "ab cd"));
        find.Open();
        find.Term = "ab";
        await find.NextAsync();
        await find.NextAsync();

        find.Term = "cd";
        await find.NextAsync();

        Assert.Equal((0, 3), moves[^1]);
    }

    [Fact]
    public void Closing_clears_the_matches_so_no_stale_highlight_survives()
    {
        var (find, _) = Build();
        find.Open();
        find.Term = "ab";

        find.Close();

        Assert.False(find.IsOpen);
        Assert.Empty(find.Matches);
        Assert.Null(find.CurrentMatch);
    }

    /// <summary>A closed bar does no work: a session with the bar shut must not pay a search on every repaint.
    /// </summary>
    [Fact]
    public void A_closed_bar_does_not_search_on_a_repaint()
    {
        var (find, _) = Build();
        find.Term = "ab";

        find.OnScreen(Screen("ab", "ab"));

        Assert.Empty(find.Matches);
    }

    /// <summary>The bug this guards: after a repaint whose re-anchor falls back (the current match's position
    /// is gone), the cursor was never moved to the match now highlighted at index 0 — so the next Enter must
    /// visit it, not skip past it to the next match, the same rule a fresh search already gets right.</summary>
    [Fact]
    public async Task A_repaint_that_falls_back_makes_the_next_next_visit_the_new_first_match()
    {
        var (find, moves) = Build();
        find.Open();
        find.Term = "ab";
        await find.NextAsync();
        await find.NextAsync();
        Assert.Equal(1, find.CurrentIndex);
        Assert.Equal([(0, 0), (1, 0)], moves);

        // The match the cursor is on (row 1) is gone; only rows 0 and 2 remain. Reanchor cannot find (1, 0)
        // and falls back to index 0.
        find.OnScreen(Screen("ab", "xx", "ab"));
        Assert.Equal(0, find.CurrentIndex);
        Assert.Equal(2, find.Matches.Count);

        await find.NextAsync();

        Assert.Equal((0, 0), moves[^1]);
    }

    /// <summary>The guard against over-fixing the above: a repaint that genuinely keeps the current match in
    /// place must not clear `_visited`, or an ordinary Next after a routine repaint would stall on the same
    /// match instead of advancing.</summary>
    [Fact]
    public async Task A_repaint_that_keeps_the_current_match_still_advances_on_the_next_next()
    {
        var (find, moves) = Build();
        find.Open();
        find.Term = "ab";
        await find.NextAsync();
        await find.NextAsync();
        Assert.Equal(1, find.CurrentIndex);
        Assert.Equal([(0, 0), (1, 0)], moves);

        find.OnScreen(Screen("ab", "ab", "ab", "ab"));
        Assert.Equal(1, find.CurrentIndex);

        await find.NextAsync();

        Assert.Equal((2, 0), moves[^1]);
    }

    [Fact]
    public void Reanchor_prefers_the_previous_position_then_falls_back_to_the_first()
    {
        var matches = new[]
        {
            ScreenRegion.FromCorners(0, 0, 0, 1),
            ScreenRegion.FromCorners(4, 7, 4, 8),
        };

        Assert.Equal(1, FindViewModel.Reanchor(matches, ScreenRegion.FromCorners(4, 7, 4, 8)));
        Assert.Equal(0, FindViewModel.Reanchor(matches, ScreenRegion.FromCorners(9, 9, 9, 9)));
        Assert.Equal(0, FindViewModel.Reanchor(matches, null));
        Assert.Equal(-1, FindViewModel.Reanchor([], ScreenRegion.FromCorners(0, 0, 0, 1)));
    }
}
