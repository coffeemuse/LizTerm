// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;

namespace LizTerm.App.Startup;

/// <summary>Whether a closing window is on its way out with the whole application, and what that means for the
/// picker. Kept pure and taking the reason as an argument, the shape MenuStrategy.Decide and
/// EngineRequirement.Decide use, because the paths that produce these reasons cannot be reached from a test.
///
/// The app returns to the picker when the last session window closes, which is right for a window the user
/// closed and wrong for one Avalonia is closing because the app is quitting: ClassicDesktopStyleApplicationLifetime
/// .DoShutdown closes every owner-less window and then gives up with <c>if (!force &amp;&amp; Windows.Count > 0)
/// { e.Cancel = true; return false; }</c>, so a picker opened from inside that close cancels the very shutdown
/// that caused it — the app closes the session and sits there, refusing to quit. App.Quit() answers this for
/// its own path by setting _quitting; every path Avalonia drives instead — the macOS application menu's Cmd+Q,
/// which calls TryShutdown(0), and an OS shutdown — is answered here, from the reason Window.CloseCore hands
/// the Closing event.</summary>
internal static class ShutdownPolicy
{
    /// <summary>True when the window is closing because the application is going away, rather than because this
    /// one window is. Both shutdown reasons count: Avalonia picks OSShutdown over ApplicationShutdown whenever
    /// the shutdown came from the platform, and the picker must stay shut for either.</summary>
    public static bool IsShutdown(WindowCloseReason reason) =>
        reason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown;

    /// <summary>Whether the window that just closed was the app's last one and the user is the one who closed
    /// it. Both callers ask this and act on their own answer: the last session window returns to the picker, the
    /// picker itself quits. <paramref name="quitting"/> is App's own flag, <paramref name="shutdownClose"/> what
    /// <see cref="IsShutdown"/> made of the close that is finishing, and <paramref name="openSessions"/> the
    /// sessions left after this one was removed.</summary>
    public static bool UserClosedLastWindow(bool quitting, bool shutdownClose, int openSessions) =>
        !quitting && !shutdownClose && openSessions == 0;
}
