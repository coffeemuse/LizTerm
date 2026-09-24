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

    /// <summary>Whether this close is the system going down, which nothing may hold — not the Quit question,
    /// and not a window's own question already on the screen: cancelling such a close is exactly what makes
    /// macOS report an interrupted logout (#169). The platform reports one as OSShutdown, the macOS backend too
    /// since Avalonia 12.1.3 (#188); <paramref name="isSystemShutdown"/> is the same answer read from the quit
    /// Apple event by Platform/MacQuitReason, which supplied it while the macOS backend reported none. A running
    /// file transfer's refusal is not a question and is not covered here.</summary>
    public static bool IsSystemShutdown(WindowCloseReason reason, bool isSystemShutdown) =>
        reason == WindowCloseReason.OSShutdown
        || (reason == WindowCloseReason.ApplicationShutdown && isSystemShutdown);

    /// <summary>A Quit asks once, for all connected sessions. It is recognised by the reason the windows it closes
    /// receive: ApplicationShutdown for a user's Quit (Cmd+Q, the Quit menu item, the picker's Quit), OSShutdown
    /// when the platform reports a logout or shutdown, which never asks. Avalonia keeps the OS flag on its
    /// ShutdownRequested event internal, so the close reason is the one public place the two are told apart, and
    /// only where the backend sets the flag. Before Avalonia 12.1.3 the macOS backend never did, so there a logout
    /// arrived as ApplicationShutdown and would have been asked like a Quit; <paramref name="isSystemShutdown"/>
    /// was the missing half of the reason, read from the quit Apple event by Platform/MacQuitReason (#169) and
    /// false everywhere else. Since 12.1.3 (#188) the macOS backend reads that same reason and a logout arrives as
    /// OSShutdown, and the second reading is kept: a system shutdown must not wait on a dialog, whichever way
    /// LizTerm hears about it. A user closing a single window is never a quit. Nothing connected,
    /// the preference off, or a quit the user has just confirmed all go through.</summary>
    public static bool ConfirmsQuit(WindowCloseReason reason, bool isSystemShutdown, int connectedSessions, bool confirmEnabled, bool confirmed) =>
        reason == WindowCloseReason.ApplicationShutdown && !isSystemShutdown && !confirmed && confirmEnabled && connectedSessions > 0;
}
