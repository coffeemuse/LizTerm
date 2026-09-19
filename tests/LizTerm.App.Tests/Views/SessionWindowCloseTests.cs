// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Views;

/// <summary>Closing a session window that is still connected asks first (#151), through the real window's
/// Closing and a fake prompt. The shutdown reasons that skip the question are ClosePolicyTests' business: no
/// headless test can close a window with one.</summary>
public class SessionWindowCloseTests
{
    private static (SessionWindow Window, SessionViewModel Vm, FakeEmulatorSession Session, FakeClosePrompt Prompt) Show(bool attachPrompt = true)
    {
        var session = new FakeEmulatorSession();
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard());
        var window = new SessionWindow { DataContext = vm };
        var prompt = new FakeClosePrompt();
        if (attachPrompt) window.AttachClosePrompt(prompt);
        window.Show();
        return (window, vm, session, prompt);
    }

    [AvaloniaFact]
    public void Keep_Connected_leaves_the_window_and_the_session_as_they_were()
    {
        var (window, _, session, prompt) = Show();
        session.RaiseConnection(ConnectionState.Connected3270);

        window.Close();

        Assert.Equal(["confirm:Close Fake?"], prompt.Calls);
        Assert.True(window.IsVisible);
        Assert.False(session.Disposed);
    }

    [AvaloniaFact]
    public void Disconnect_closes_the_window()
    {
        var (window, _, session, prompt) = Show();
        session.RaiseConnection(ConnectionState.Connected3270);
        prompt.Disconnect = true;

        window.Close();

        Assert.Single(prompt.Calls);
        Assert.False(window.IsVisible);
    }

    /// <summary>Nothing to lose: no prompt for a window that never connected, or is still on its way.</summary>
    [AvaloniaTheory]
    [InlineData(ConnectionState.Disconnected)]
    [InlineData(ConnectionState.TcpPending)]
    [InlineData(ConnectionState.Reconnecting)]
    public void A_window_that_is_not_connected_closes_without_asking(ConnectionState state)
    {
        var (window, _, session, prompt) = Show();
        session.RaiseConnection(state);

        window.Close();

        Assert.Empty(prompt.Calls);
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void With_the_preference_off_a_connected_window_closes_without_asking()
    {
        var (window, vm, session, prompt) = Show();
        vm.Settings.ConfirmCloseWhileConnected = false;
        session.RaiseConnection(ConnectionState.Connected3270);

        window.Close();

        Assert.Empty(prompt.Calls);
        Assert.False(window.IsVisible);
    }

    /// <summary>A window built without a prompt, as every other test builds one, closes as it always did.</summary>
    [AvaloniaFact]
    public void A_window_without_a_prompt_closes_silently()
    {
        var (window, _, session, _) = Show(attachPrompt: false);
        session.RaiseConnection(ConnectionState.Connected3270);

        window.Close();

        Assert.False(window.IsVisible);
    }

    /// <summary>A second Cmd+W while the question is up asks nothing more, and the one answer decides.</summary>
    [AvaloniaFact]
    public void A_second_close_while_the_prompt_is_open_asks_nothing_more()
    {
        var (window, _, session, prompt) = Show();
        session.RaiseConnection(ConnectionState.Connected3270);
        prompt.Gate = new TaskCompletionSource();
        prompt.Disconnect = true;

        window.Close();
        window.Close();
        Assert.Single(prompt.Calls);
        Assert.True(window.IsVisible);

        prompt.Gate.SetResult();
        Dispatcher.UIThread.RunJobs();

        Assert.Single(prompt.Calls);
        Assert.False(window.IsVisible);
    }

    /// <summary>Keep Connected is an answer for this attempt only: the next close asks again.</summary>
    [AvaloniaFact]
    public void After_Keep_Connected_the_next_close_asks_again()
    {
        var (window, _, session, prompt) = Show();
        session.RaiseConnection(ConnectionState.Connected3270);

        window.Close();
        prompt.Disconnect = true;
        window.Close();

        Assert.Equal(2, prompt.Calls.Count);
        Assert.False(window.IsVisible);
    }
}
