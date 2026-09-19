// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;

namespace LizTerm.App.Startup;

/// <summary>Whether closing a session window, or quitting, asks first because a session is still connected
/// (#151). Pure and beside <see cref="ShutdownPolicy"/> for the reason that one is: the shutdown reasons and the
/// OS-shutdown flag come from paths a test cannot drive, so the decision is asserted on its own and the callers
/// stay thin. Avalonia's Closing and ShutdownRequested are synchronous, so both callers take the same shape:
/// cancel, ask, and on Disconnect close (or quit) again with <c>confirmed</c> set for that one attempt.</summary>
internal static class ClosePolicy
{
    /// <summary>A window close asks only when the user is closing this one window, it is connected, the
    /// preference is on, and the user has not just said Disconnect. A shutdown close never asks: an OS shutdown
    /// must not wait on a dialog, and a Quit that asked once must not be asked again by every window it closes.</summary>
    public static bool ConfirmsWindowClose(WindowCloseReason reason, bool connected, bool confirmEnabled, bool confirmed) =>
        !confirmed && confirmEnabled && connected && !ShutdownPolicy.IsShutdown(reason);

    /// <summary>A Quit asks once, for all connected sessions. It is recognised by the reason the windows it closes
    /// receive: ApplicationShutdown for a user's Quit (Cmd+Q, the Quit menu item, the picker's Quit), OSShutdown
    /// when the platform is logging out or shutting down, which never asks. Avalonia keeps the OS flag on its
    /// ShutdownRequested event internal, so the close reason is the one public place the two are told apart. A
    /// user closing a single window is never a quit. Nothing connected, the preference off, or a quit the user
    /// has just confirmed all go through.</summary>
    public static bool ConfirmsQuit(WindowCloseReason reason, int connectedSessions, bool confirmEnabled, bool confirmed) =>
        reason == WindowCloseReason.ApplicationShutdown && !confirmed && confirmEnabled && connectedSessions > 0;
}
