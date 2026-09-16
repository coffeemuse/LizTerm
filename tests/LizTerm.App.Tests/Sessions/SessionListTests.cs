// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Sessions;
using LizTerm.App.Tests.Fakes;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Sessions;

public class SessionListTests
{
    [Fact]
    public void Positions_follow_opening_order_and_closing_moves_later_sessions_up()
    {
        var list = new SessionList();
        var (a, _, _) = TestSessions.Create("A");
        var (b, _, _) = TestSessions.Create("B");
        var (c, _, _) = TestSessions.Create("C");
        list.Add(a);
        list.Add(b);
        list.Add(c);

        Assert.Equal([a, b, c], list.Entries);
        Assert.Equal(2, list.PositionOf(b));

        list.Remove(a);

        Assert.Equal(2, list.Count);
        Assert.Equal(1, list.PositionOf(b));
        Assert.Equal(2, list.PositionOf(c));
        Assert.Null(list.PositionOf(a));
    }

    [Fact]
    public void The_tenth_session_is_position_ten_and_the_eleventh_has_none()
    {
        var list = new SessionList();
        var entries = Enumerable.Range(1, 11).Select(i => TestSessions.Create($"S{i}").Entry).ToList();
        entries.ForEach(list.Add);

        Assert.Equal(10, list.PositionOf(entries[9]));
        Assert.Null(list.PositionOf(entries[10]));
    }

    /// <summary>A session never activated is the least recent, so a new window does not become Previous before
    /// anyone has used it.</summary>
    [Fact]
    public void Current_and_previous_follow_activation()
    {
        var list = new SessionList();
        var (a, _, _) = TestSessions.Create("A");
        var (b, _, _) = TestSessions.Create("B");
        var (c, _, _) = TestSessions.Create("C");
        list.Add(a);
        Assert.Same(a, list.Current);
        Assert.Null(list.Previous);

        list.Add(b);
        list.Add(c);
        list.Activated(c);
        list.Activated(b);

        Assert.Same(b, list.Current);
        Assert.Same(c, list.Previous);

        list.Remove(b);

        Assert.Same(c, list.Current);
        Assert.Same(a, list.Previous);
    }

    /// <summary>What App keeps its own windows (the picker, Preferences) on top by: any open session kept on top.</summary>
    [Fact]
    public void Any_keep_on_top_is_true_while_some_open_session_is_kept_on_top()
    {
        var list = new SessionList();
        var (a, _, aHost) = TestSessions.Create("A");
        var (b, _, bHost) = TestSessions.Create("B");
        list.Add(a);
        list.Add(b);
        Assert.False(list.AnyKeepOnTop);

        bHost.KeepOnTop = true;
        Assert.True(list.AnyKeepOnTop);
        aHost.KeepOnTop = true;
        bHost.KeepOnTop = false;
        Assert.True(list.AnyKeepOnTop);

        list.Remove(a);
        Assert.False(list.AnyKeepOnTop);
    }

    [Fact]
    public void Changed_fires_on_add_remove_activation_connection_and_keep_on_top()
    {
        var list = new SessionList();
        var (a, _, _) = TestSessions.Create("A");
        var (b, bSession, bHost) = TestSessions.Create("B");
        var changes = 0;
        list.Changed += (_, _) => changes++;

        list.Add(a);
        list.Add(b);
        Assert.Equal(2, changes);

        list.Activated(b);
        Assert.Equal(3, changes);
        list.Activated(b); // already Current: nothing changed
        Assert.Equal(3, changes);

        bSession.RaiseConnection(ConnectionState.Disconnected);
        Assert.Equal(4, changes);

        bHost.KeepOnTop = true;
        Assert.Equal(5, changes);

        list.Remove(b);
        Assert.Equal(6, changes);
    }

    [Fact]
    public void A_removed_session_raises_nothing_more()
    {
        var list = new SessionList();
        var (a, aSession, aHost) = TestSessions.Create("A");
        list.Add(a);
        list.Remove(a);
        var changes = 0;
        list.Changed += (_, _) => changes++;

        aSession.RaiseConnection(ConnectionState.Disconnected);
        aHost.KeepOnTop = true;
        list.Activated(a);
        list.Remove(a);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Adding_the_same_session_twice_throws()
    {
        var list = new SessionList();
        var (a, _, _) = TestSessions.Create("A");
        list.Add(a);

        Assert.Throws<InvalidOperationException>(() => list.Add(a));
    }

    /// <summary>Bringing each window activates it, so the hosts here call Activated exactly as a real window's
    /// Activated event would. Reverse use order replays the same use order: Current ends in front and Previous is
    /// unchanged. The minimised session (here the least recent, so no order it held is disturbed) is skipped.</summary>
    [Fact]
    public void Bring_all_to_front_raises_in_reverse_use_order_skipping_minimised_and_keeps_previous()
    {
        var list = new SessionList();
        var (a, _, aHost) = TestSessions.Create("A");
        var (b, _, bHost) = TestSessions.Create("B");
        var (c, _, cHost) = TestSessions.Create("C");
        var (d, _, dHost) = TestSessions.Create("D");
        var order = new List<string>();
        foreach (var (entry, host) in new[] { (a, aHost), (b, bHost), (c, cHost), (d, dHost) })
        {
            list.Add(entry);
            host.OnBring = () =>
            {
                order.Add(entry.Session.Profile.Name);
                list.Activated(entry);
            };
        }
        list.Activated(d);
        list.Activated(c);
        list.Activated(b); // use order: B, C, D, A
        aHost.IsMinimized = true;

        list.BringAllToFront();

        Assert.Equal(["D", "C", "B"], order);
        Assert.Same(b, list.Current);
        Assert.Same(c, list.Previous);
        Assert.Equal(0, aHost.BringCount);
    }
}
