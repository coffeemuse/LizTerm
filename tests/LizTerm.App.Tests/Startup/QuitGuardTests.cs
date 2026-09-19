// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using LizTerm.App.Sessions;
using LizTerm.App.Startup;
using LizTerm.App.Tests.Fakes;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Startup;

/// <summary>Quit's half of #151, through the seam every owner-less window's Closing calls with its close reason:
/// the count of connected sessions, one question for all of them, every window held while it is up, and the quit
/// repeated past the guard on Disconnect. A quit here is the pass Avalonia's TryShutdown makes over the windows,
/// played by calling Holds once per window.</summary>
public class QuitGuardTests
{
    private const WindowCloseReason Quit = WindowCloseReason.ApplicationShutdown;

    private static (QuitGuard Guard, FakeClosePrompt Prompt, List<string> Quits) Build(bool confirmEnabled = true, params ConnectionState[] states)
    {
        var sessions = new SessionList();
        foreach (var (state, i) in states.Select((s, i) => (s, i)))
            sessions.Add(TestSessions.Create($"S{i}", state: state).Entry);
        var prompt = new FakeClosePrompt();
        var quits = new List<string>();
        QuitGuard guard = null!;
        guard = new QuitGuard(sessions, () => confirmEnabled, _ => prompt, () =>
        {
            // What App.Quit does: TryShutdown, which closes every window with ApplicationShutdown.
            var held = states.Count(_ => guard.Holds(Quit));
            quits.Add(held == 0 ? "quit" : $"quit:held:{held}");
        });
        return (guard, prompt, quits);
    }

    [Fact]
    public void With_nothing_connected_the_windows_close_unasked()
    {
        var (guard, prompt, _) = Build(true, ConnectionState.Disconnected, ConnectionState.TcpPending);

        Assert.False(guard.Holds(Quit));
        Assert.False(guard.Holds(Quit));
        Assert.Empty(prompt.Calls);
    }

    /// <summary>Every window in the pass is held, the connected and the disconnected alike: Keep Connected must
    /// leave everything as it was, and a window that closed during the pass would be gone. The answer is held
    /// back until the pass is over, as a modal dialog's is.</summary>
    [Fact]
    public async Task Keep_Connected_holds_every_window_and_asks_once_about_the_connected_ones()
    {
        var (guard, prompt, quits) = Build(true, ConnectionState.Connected3270, ConnectionState.Disconnected, ConnectionState.ConnectedNvt);
        prompt.Gate = new TaskCompletionSource();

        Assert.True(guard.Holds(Quit));
        Assert.True(guard.Holds(Quit));
        Assert.True(guard.Holds(Quit));
        prompt.Gate.SetResult();
        await Task.Yield();

        Assert.Equal(["confirm:Quit LizTerm?"], prompt.Calls);
        Assert.Equal("2 sessions are still connected. Quitting will disconnect them immediately.", prompt.LastRequest!.Message);
        Assert.Empty(quits);
    }

    [Fact]
    public void Disconnect_quits_again_and_that_pass_holds_nothing()
    {
        var (guard, prompt, quits) = Build(true, ConnectionState.Connected3270, ConnectionState.Disconnected);
        prompt.Disconnect = true;

        Assert.True(guard.Holds(Quit));

        Assert.Single(prompt.Calls);
        Assert.Equal(["quit"], quits);
    }

    /// <summary>The bypass is for the one quit Disconnect asked for: if that quit is refused elsewhere (a running
    /// transfer's dialog, say), the next Cmd+Q asks again rather than slipping through.</summary>
    [Fact]
    public void After_a_confirmed_quit_the_next_pass_asks_again()
    {
        var (guard, prompt, _) = Build(true, ConnectionState.Connected3270);
        prompt.Disconnect = true;
        guard.Holds(Quit);

        Assert.True(guard.Holds(Quit));
        Assert.Equal(2, prompt.Calls.Count);
    }

    [Fact]
    public void With_the_preference_off_a_connected_session_does_not_hold_the_quit()
    {
        var (guard, prompt, _) = Build(false, ConnectionState.Connected3270);

        Assert.False(guard.Holds(Quit));
        Assert.Empty(prompt.Calls);
    }

    /// <summary>An OS shutdown, or the user closing one window, is never Quit's question.</summary>
    [Theory]
    [InlineData(WindowCloseReason.OSShutdown)]
    [InlineData(WindowCloseReason.WindowClosing)]
    public void Any_other_reason_is_not_held(WindowCloseReason reason)
    {
        var (guard, prompt, _) = Build(true, ConnectionState.Connected3270);

        Assert.False(guard.Holds(reason));
        Assert.Empty(prompt.Calls);
    }

    /// <summary>A second Cmd+Q while the question is up is still held, and asks nothing more.</summary>
    [Fact]
    public async Task A_second_pass_while_the_question_is_up_asks_nothing_more()
    {
        var (guard, prompt, quits) = Build(true, ConnectionState.Connected3270);
        prompt.Gate = new TaskCompletionSource();
        prompt.Disconnect = true;

        Assert.True(guard.Holds(Quit));
        Assert.True(guard.Holds(Quit));
        Assert.Single(prompt.Calls);
        Assert.Empty(quits);

        prompt.Gate.SetResult();
        await Task.Yield();

        Assert.Single(prompt.Calls);
        Assert.Equal(["quit"], quits);
    }

    /// <summary>The question opens over the session the user was last in, so it appears where they are looking.</summary>
    [Fact]
    public void The_question_is_asked_over_the_current_session()
    {
        var sessions = new SessionList();
        var first = TestSessions.Create("First").Entry;
        var second = TestSessions.Create("Second").Entry;
        sessions.Add(first);
        sessions.Add(second);
        sessions.Activated(second);
        var owners = new List<SessionEntry>();
        var guard = new QuitGuard(sessions, () => true, entry => { owners.Add(entry); return new FakeClosePrompt(); }, () => { });

        guard.Holds(Quit);

        Assert.Equal([second], owners);
    }
}
