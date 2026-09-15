// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Sessions;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>Session switching spec §5.2 and §5.4, without a window.</summary>
public class SessionSwitcherViewModelTests
{
    /// <summary>A list of A (this window), B and C, with A the current session and C the previous one.</summary>
    private static (SessionList List, SessionEntry A, SessionEntry B, SessionEntry C, SessionSwitcherViewModel Vm) Three()
    {
        var list = new SessionList();
        var (a, _, _) = TestSessions.Create("TSO", "tk5.local");
        var (b, _, _) = TestSessions.Create("CICS", "zxplore.example", note: "No live data", tags: ["ZOS"]);
        var (c, _, _) = TestSessions.Create("VM370", "vm370.local");
        list.Add(a);
        list.Add(b);
        list.Add(c);
        list.Activated(c);
        list.Activated(a);
        return (list, a, b, c, new SessionSwitcherViewModel(list, a));
    }

    [Fact]
    public void Opening_lists_every_session_and_selects_the_previous_one()
    {
        var (_, a, b, c, vm) = Three();

        vm.Open();

        Assert.True(vm.IsOpen);
        Assert.Equal([a, b, c], vm.Rows.Select(row => row.Entry));
        Assert.Same(c, vm.Selected!.Entry);
        Assert.True(vm.Selected.IsSelected);
        Assert.True(vm.DigitsJump);
        Assert.Equal(SessionSwitcherViewModel.JumpHint, vm.Hint);
    }

    [Fact]
    public void Opening_with_one_session_selects_it()
    {
        var list = new SessionList();
        var (only, _, _) = TestSessions.Create("TSO");
        list.Add(only);
        var vm = new SessionSwitcherViewModel(list, only);

        vm.Open();

        Assert.Same(only, vm.Selected!.Entry);
    }

    [Fact]
    public void A_digit_jumps_only_while_the_filter_is_empty()
    {
        var (_, _, b, _, vm) = Three();
        vm.Open();

        Assert.Same(b, vm.TryDigit('2'));

        vm.Term = "v";
        Assert.False(vm.DigitsJump);
        Assert.Equal(SessionSwitcherViewModel.FilterHint, vm.Hint);
        Assert.Null(vm.TryDigit('2'));

        vm.Term = "";
        Assert.Same(b, vm.TryDigit('2'));
    }

    [Fact]
    public void A_digit_with_no_session_at_its_position_does_nothing()
    {
        var (_, _, _, _, vm) = Three();
        vm.Open();

        Assert.Null(vm.TryDigit('4'));
        Assert.Null(vm.TryDigit('0'));
        Assert.Null(vm.TryDigit('x'));
    }

    [Fact]
    public void Zero_is_the_tenth_session_and_the_eleventh_has_no_number()
    {
        var list = new SessionList();
        var entries = Enumerable.Range(1, 11).Select(i => TestSessions.Create($"S{i}").Entry).ToList();
        entries.ForEach(list.Add);
        var vm = new SessionSwitcherViewModel(list, entries[0]);
        vm.Open();

        Assert.Same(entries[9], vm.TryDigit('0'));
        Assert.Equal("0", vm.Rows[9].Number);
        Assert.Null(vm.Rows[10].Number);
        Assert.False(vm.Rows[10].HasNumber);
    }

    [Fact]
    public void A_digit_is_ignored_while_closed()
    {
        var (_, _, _, _, vm) = Three();

        Assert.Null(vm.TryDigit('1'));
        Assert.Null(vm.Choose());
    }

    [Theory]
    [InlineData("cics", true)]    // name
    [InlineData("ZXPLORE", true)] // host
    [InlineData("zos", true)]     // tag
    [InlineData("LIVE", true)]    // note
    [InlineData("", true)]
    [InlineData("vm370", false)]
    public void The_filter_matches_name_host_tags_and_note_ignoring_case(string term, bool expected)
    {
        var (entry, _, _) = TestSessions.Create("CICS", "zxplore.example", note: "No live data", tags: ["ZOS"]);

        Assert.Equal(expected, SwitcherFilter.Matches(entry, term));
    }

    [Fact]
    public void Changing_the_filter_selects_the_first_match_and_dims_the_numbers()
    {
        var (_, _, _, c, vm) = Three();
        vm.Open();

        vm.Term = "vm";

        Assert.Equal([c], vm.Rows.Select(row => row.Entry));
        Assert.Same(c, vm.Selected!.Entry);
        Assert.False(vm.Rows[0].NumberActive);
        Assert.Equal("3", vm.Rows[0].Number); // its place in opening order, not in the filtered list
    }

    [Fact]
    public void Up_and_down_stop_at_the_ends()
    {
        var (_, a, b, c, vm) = Three();
        vm.Open(); // on C, the last row

        vm.MoveDown();
        Assert.Same(c, vm.Selected!.Entry);

        vm.MoveUp();
        Assert.Same(b, vm.Selected!.Entry);
        Assert.False(vm.Rows[2].IsSelected);
        vm.MoveUp();
        vm.MoveUp();
        Assert.Same(a, vm.Selected!.Entry);
    }

    [Fact]
    public void Enter_chooses_the_selected_session_and_nothing_when_no_row_matches()
    {
        var (_, _, _, c, vm) = Three();
        vm.Open();

        Assert.Same(c, vm.Choose());

        vm.Term = "zzz";
        Assert.Empty(vm.Rows);
        Assert.Null(vm.Choose());
    }

    [Fact]
    public void Rows_carry_the_connection_mark_and_the_first_flag_that_applies()
    {
        var list = new SessionList();
        var (own, _, _) = TestSessions.Create("TSO");
        var (down, _, _) = TestSessions.Create("CICS", state: ConnectionState.Disconnected);
        var (pinned, _, pinnedHost) = TestSessions.Create("IMON");
        var (plain, _, _) = TestSessions.Create("VM370");
        pinnedHost.KeepOnTop = true;
        foreach (var entry in new[] { own, down, pinned, plain }) list.Add(entry);
        var vm = new SessionSwitcherViewModel(list, own);

        vm.Open();

        Assert.Equal(["●", "○", "●", "●"], vm.Rows.Select(row => row.ConnectionMark));
        Assert.Equal(new string?[] { "This window", "Disconnected", "On top", null }, vm.Rows.Select(row => row.Flag));
        Assert.All(vm.Rows, row => Assert.True(row.NumberActive));
    }

    [Fact]
    public void A_change_while_open_keeps_the_selected_session_or_falls_to_the_first_row()
    {
        var (list, a, b, c, vm) = Three();
        vm.Open();
        vm.MoveUp(); // B

        list.Remove(c);
        Assert.Same(b, vm.Selected!.Entry);

        list.Remove(b);
        Assert.Same(a, vm.Selected!.Entry);
    }

    [Fact]
    public void Closing_stops_listening_and_empties_the_rows()
    {
        var (list, _, _, _, vm) = Three();
        vm.Open();

        vm.Close();
        list.Add(TestSessions.Create("NEW").Entry);

        Assert.False(vm.IsOpen);
        Assert.Empty(vm.Rows);
        Assert.Null(vm.Selected);
    }

    [Fact]
    public void Reopening_clears_the_filter()
    {
        var (_, _, _, _, vm) = Three();
        vm.Open();
        vm.Term = "vm";
        vm.Close();

        vm.Open();

        Assert.Equal("", vm.Term);
        Assert.Equal(3, vm.Rows.Count);
    }
}
