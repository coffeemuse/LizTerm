// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using LizTerm.App.Startup;

namespace LizTerm.App.Tests.Startup;

/// <summary>Whether a close, or a Quit, asks first while a session is connected (#151). Pure, like
/// ShutdownPolicy, and for the same reason: the shutdown reasons and the OS-shutdown flag come from paths no
/// headless test can drive.</summary>
public class ClosePolicyTests
{
    [Fact]
    public void A_user_close_of_a_connected_window_asks()
    {
        Assert.True(ClosePolicy.ConfirmsWindowClose(WindowCloseReason.WindowClosing, connected: true, confirmEnabled: true, confirmed: false));
    }

    /// <summary>Nothing to lose: a window that is disconnected, or still on its way to the host, goes silently.</summary>
    [Fact]
    public void A_window_that_is_not_connected_closes_without_asking()
    {
        Assert.False(ClosePolicy.ConfirmsWindowClose(WindowCloseReason.WindowClosing, connected: false, confirmEnabled: true, confirmed: false));
    }

    [Fact]
    public void The_preference_turns_the_question_off()
    {
        Assert.False(ClosePolicy.ConfirmsWindowClose(WindowCloseReason.WindowClosing, connected: true, confirmEnabled: false, confirmed: false));
    }

    /// <summary>The second close, after Disconnect was chosen, must go through or the window could never close.</summary>
    [Fact]
    public void A_close_the_user_already_confirmed_goes_through()
    {
        Assert.False(ClosePolicy.ConfirmsWindowClose(WindowCloseReason.WindowClosing, connected: true, confirmEnabled: true, confirmed: true));
    }

    /// <summary>The issue's hard rule: an OS shutdown never waits on this prompt, and a Quit that already asked
    /// once must not be asked again by every window it closes.</summary>
    [Theory]
    [InlineData(WindowCloseReason.ApplicationShutdown)]
    [InlineData(WindowCloseReason.OSShutdown)]
    public void A_shutdown_close_never_asks(WindowCloseReason reason)
    {
        Assert.False(ClosePolicy.ConfirmsWindowClose(reason, connected: true, confirmEnabled: true, confirmed: false));
    }

    /// <summary>Quit closes every window with ApplicationShutdown, and that reason is the only one that can mean
    /// a user's Quit: Avalonia gives OSShutdown when the platform is logging out or shutting down.</summary>
    [Fact]
    public void A_user_quit_with_a_connected_session_asks()
    {
        Assert.True(ClosePolicy.ConfirmsQuit(WindowCloseReason.ApplicationShutdown, isSystemShutdown: false, connectedSessions: 1, confirmEnabled: true, confirmed: false));
    }

    [Fact]
    public void A_quit_with_nothing_connected_goes_through()
    {
        Assert.False(ClosePolicy.ConfirmsQuit(WindowCloseReason.ApplicationShutdown, isSystemShutdown: false, connectedSessions: 0, confirmEnabled: true, confirmed: false));
    }

    /// <summary>The issue's hard rule: an OS shutdown never waits on this prompt.</summary>
    [Fact]
    public void An_os_shutdown_never_asks()
    {
        Assert.False(ClosePolicy.ConfirmsQuit(WindowCloseReason.OSShutdown, isSystemShutdown: false, connectedSessions: 2, confirmEnabled: true, confirmed: false));
    }

    /// <summary>A window the user is closing on its own is the window question's business, not Quit's.</summary>
    [Theory]
    [InlineData(WindowCloseReason.WindowClosing)]
    [InlineData(WindowCloseReason.OwnerWindowClosing)]
    [InlineData(WindowCloseReason.Undefined)]
    public void Closing_one_window_is_not_a_quit(WindowCloseReason reason)
    {
        Assert.False(ClosePolicy.ConfirmsQuit(reason, isSystemShutdown: false, connectedSessions: 2, confirmEnabled: true, confirmed: false));
    }

    [Fact]
    public void The_preference_and_a_confirmed_quit_both_let_it_through()
    {
        Assert.False(ClosePolicy.ConfirmsQuit(WindowCloseReason.ApplicationShutdown, isSystemShutdown: false, connectedSessions: 2, confirmEnabled: false, confirmed: false));
        Assert.False(ClosePolicy.ConfirmsQuit(WindowCloseReason.ApplicationShutdown, isSystemShutdown: false, connectedSessions: 2, confirmEnabled: true, confirmed: true));
    }

    /// <summary>#169. The macOS backend closes a logout's windows with ApplicationShutdown, since it never sets
    /// the OS-shutdown flag, so the reason alone cannot tell a logout from a Cmd+Q there. Told separately that
    /// the login session is ending, the quit goes through unasked, as OSShutdown does everywhere else: a system
    /// shutdown must not wait on a dialog, whichever way LizTerm hears about it.</summary>
    [Fact]
    public void A_macos_logout_arriving_as_an_application_shutdown_never_asks()
    {
        Assert.False(ClosePolicy.ConfirmsQuit(WindowCloseReason.ApplicationShutdown, isSystemShutdown: true, connectedSessions: 3, confirmEnabled: true, confirmed: false));
    }

    /// <summary>The flag is off for a user's Quit on every platform, so the question survives it.</summary>
    [Fact]
    public void A_quit_that_is_not_a_system_shutdown_still_asks()
    {
        Assert.True(ClosePolicy.ConfirmsQuit(WindowCloseReason.ApplicationShutdown, isSystemShutdown: false, connectedSessions: 3, confirmEnabled: true, confirmed: false));
    }
}
