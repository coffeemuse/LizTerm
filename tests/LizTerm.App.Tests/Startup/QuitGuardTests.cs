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

    private static (QuitGuard Guard, FakeClosePrompt Prompt, List<string> Quits) Build(bool confirmEnabled = true, params ConnectionState[] states) =>
        Build(confirmEnabled, systemShutdown: false, states);

    /// <param name="systemShutdown">What the platform says about this pass: on macOS a logout arrives as an
    /// ApplicationShutdown like any Quit, and this is the only thing that tells the two apart (#169).</param>
    private static (QuitGuard Guard, FakeClosePrompt Prompt, List<string> Quits) Build(bool confirmEnabled, bool systemShutdown, ConnectionState[] states)
    {
        var sessions = new SessionList();
        foreach (var (state, i) in states.Select((s, i) => (s, i)))
            sessions.Add(TestSessions.Create($"S{i}", state: state).Entry);
        var prompt = new FakeClosePrompt();
        var quits = new List<string>();
        QuitGuard guard = null!;
        guard = new QuitGuard(sessions, () => confirmEnabled, () => systemShutdown, _ => prompt, () =>
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
        var guard = new QuitGuard(sessions, () => true, () => false, entry => { owners.Add(entry); return new FakeClosePrompt(); }, () => { });

        guard.Holds(Quit);

        Assert.Equal([second], owners);
    }

    /// <summary>A question that could not be put up holds the quit (a failed question is not Disconnect) and says
    /// so on the session it would have opened over, so the user hears why Cmd+Q did nothing. The next Cmd+Q asks
    /// again rather than waiting on a question that never opened.</summary>
    [Fact]
    public void A_prompt_that_fails_holds_the_quit_and_reports_it()
    {
        var sessions = new SessionList();
        var entry = TestSessions.Create("S", state: ConnectionState.Connected3270).Entry;
        sessions.Add(entry);
        var prompt = new FakeClosePrompt { Exception = new InvalidOperationException("no dialog") };
        var quits = 0;
        var guard = new QuitGuard(sessions, () => true, () => false, _ => prompt, () => quits++);

        Assert.True(guard.Holds(Quit));

        Assert.Single(prompt.Calls);
        Assert.Equal(0, quits);
        Assert.Equal("Could not ask before quitting: no dialog", entry.Session.ErrorMessage);
        Assert.False(guard.IsAsking);
        Assert.True(guard.Holds(Quit));
        Assert.Equal(2, prompt.Calls.Count);
    }

    /// <summary>A session window's own question is up: a Quit meanwhile holds every window and asks nothing, so
    /// two questions never share the screen; the open one decides for its window, and the next Quit asks its own.</summary>
    [Fact]
    public async Task A_window_question_up_holds_the_quit_without_a_second_question()
    {
        var (guard, prompt, quits) = Build(true, ConnectionState.Connected3270);
        var gate = new TaskCompletionSource<bool>();
        var repeated = 0;
        guard.NewWindowQuestion().Ask(() => gate.Task, () => repeated++, _ => { });

        Assert.True(guard.Holds(Quit));
        Assert.Empty(prompt.Calls);
        Assert.Empty(quits);

        gate.SetResult(false);
        await Task.Yield();
        Assert.Equal(0, repeated);
        Assert.True(guard.Holds(Quit));
        Assert.Single(prompt.Calls);
    }

    /// <summary>#169. A macOS logout closes the windows with ApplicationShutdown, exactly as a Cmd+Q does, so
    /// without this the guard would put its question up and macOS would report the logout as interrupted. Told
    /// that the login session is ending, it holds nothing and asks nothing: the sessions go, as they do on a
    /// Windows or Linux logout, where Avalonia gives the windows OSShutdown instead.</summary>
    [Fact]
    public void A_system_shutdown_closes_every_window_unasked()
    {
        var (guard, prompt, _) = Build(confirmEnabled: true, systemShutdown: true,
            [ConnectionState.Connected3270, ConnectionState.ConnectedNvt]);

        Assert.False(guard.Holds(Quit));
        Assert.False(guard.Holds(Quit));
        Assert.Empty(prompt.Calls);
        Assert.False(guard.IsAsking);
    }
}
