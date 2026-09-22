// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Logging;
using static LizTerm.App.Platform.LibObjc;

namespace LizTerm.App.Platform;

/// <summary>Tells a macOS logout from a user's Quit, so a logout is not held up by the question #151 added
/// (#169). Avalonia closes windows with WindowCloseReason.OSShutdown where the platform reports a logout or a
/// shutdown, and Startup/ClosePolicy never asks then; the macOS backend never reports one
/// (AvaloniaNativeApplicationPlatform raises ShutdownRequested with a plain ShutdownRequestedEventArgs), so there
/// a logout arrived as an ordinary Quit, was asked, and AppKit — reading the NSTerminateCancel that the unanswered
/// question produces as the app refusing to go — put up "LizTerm interrupted logout" beside LizTerm's own
/// question. This is the missing half of the reason, read where Avalonia cannot supply it.
///
/// AppKit calls applicationShouldTerminate: from inside its handler for the quit Apple event, and
/// -[AvnAppDelegate applicationShouldTerminate:] is one call to the managed TryShutdown, which raises
/// ShutdownRequested and makes the whole window pass before it answers. So every window's Closing — and the
/// Startup/QuitGuard consulted from it — runs inside that handler, where currentAppleEvent is still the quit
/// event and its kAEQuitReason parameter says why loginwindow sent it. A user's Cmd+Q, the Quit menu item and
/// every managed TryShutdown carry no Apple event at all, which is how the two are told apart.
///
/// Nothing here is installed or replaced: it is a read, taken afresh each time, so there is no state to go stale
/// and no method in front of AppKit's. Selectors are registered per call rather than cached, because a quit
/// happens once and a static IntPtr initialised eagerly would P/Invoke libobjc on Linux. Every failure — a
/// runtime that cannot be read, a class that is not there, an exception — answers false, which is the behaviour
/// that shipped in 0.7.0: ask, and let macOS say the logout was interrupted.</summary>
internal static class MacQuitReason
{
    /// <summary>kCoreEventClass, 'aevt'.</summary>
    internal const uint QuitEventClass = 0x61657674;

    /// <summary>kAEQuitApplication, 'quit'.</summary>
    internal const uint QuitEventId = 0x71756974;

    /// <summary>kAEQuitReason, 'why?': the quit event's parameter naming what led to it being sent. A
    /// parameter, not an attribute — AERegistry.h says so in as many words ("in a kAEQuitApplication event,
    /// this parameter if present is the reason the quit is being sent"), and every attribute keyword in
    /// AEDataModel.h is spelled ...Attr, which this one is not.</summary>
    internal const uint QuitReasonKeyword = 0x7768793F;

    /// <summary>kAEQuitAll, 'quia'. Every application is being quit and the user stays logged in.</summary>
    internal const uint QuitAll = 0x71756961;

    /// <summary>kAEReallyLogOut, 'rlgo': the reason loginwindow gives each application as it logs the user out.</summary>
    internal const uint ReallyLogOut = 0x726C676F;

    /// <summary>kAELogOut, 'logo'.</summary>
    internal const uint LogOut = 0x6C6F676F;

    /// <summary>kAEShutDown, 'shut'.</summary>
    internal const uint ShutDown = 0x73687574;

    /// <summary>kAERestart, 'rest'.</summary>
    internal const uint Restart = 0x72657374;

    /// <summary>kAEShowShutdownDialog, 'rsdn'.</summary>
    internal const uint ShowShutdownDialog = 0x7273646E;

    /// <summary>kAEShowRestartDialog, 'rrst'.</summary>
    internal const uint ShowRestartDialog = 0x72727374;

    /// <summary>The rule, on its own so a test can pin it: a quit whose reason names the login session or the
    /// machine ending. AERegistry.h lists kAEQuitAll, kAEShutDown, kAERestart and kAEReallyLogOut as the reasons
    /// an application can be given, and the logout and dialog spellings beside them; all but Quit All mean the
    /// user is on their way out and LizTerm must not stand in the way. Quit All leaves the user logged in, so it
    /// is a Quit like any other and a connected session is still worth asking about. Reason 0 is no reason
    /// read at all, which is every quit nobody gave one for — an AppleScript quit, say.</summary>
    internal static bool EndsTheSession(uint eventClass, uint eventId, uint reason) =>
        eventClass == QuitEventClass && eventId == QuitEventId
        && reason is ReallyLogOut or LogOut or ShutDown or Restart or ShowShutdownDialog or ShowRestartDialog;

    /// <summary>Whether the quit being handled right now is the system logging out, restarting or shutting down.
    /// Called from QuitGuard, inside the window pass; false off macOS without touching libobjc.</summary>
    public static bool IsSystemShutdown(bool isMacOS)
    {
        if (!isMacOS) return false;
        try
        {
            var manager = objc_getClass("NSAppleEventManager");
            if (manager == IntPtr.Zero) return false;
            var shared = objc_msgSend_IntPtr(manager, sel_registerName("sharedAppleEventManager"));
            if (shared == IntPtr.Zero) return false;
            // Nil unless an Apple event is being handled: a Cmd+Q, the Quit menu item and App.Quit all land here.
            var quit = objc_msgSend_IntPtr(shared, sel_registerName("currentAppleEvent"));
            if (quit == IntPtr.Zero) return false;
            var eventClass = objc_msgSend_uint(quit, sel_registerName("eventClass"));
            var eventId = objc_msgSend_uint(quit, sel_registerName("eventID"));
            // The reason is a parameter of the quit event (see QuitReasonKeyword), and an event's parameters
            // and its attributes are separate slots that do not fall back to one another, so the parameter is
            // the one to read. The attribute is tried after it only because code in the wild writes it there.
            var descriptor = objc_msgSend_IntPtr(quit, sel_registerName("paramDescriptorForKeyword:"), QuitReasonKeyword);
            if (descriptor == IntPtr.Zero)
                descriptor = objc_msgSend_IntPtr(quit, sel_registerName("attributeDescriptorForKeyword:"), QuitReasonKeyword);
            var reason = descriptor == IntPtr.Zero ? 0u : objc_msgSend_uint(descriptor, sel_registerName("enumCodeValue"));
            if (EndsTheSession(eventClass, eventId, reason)) return true;
            // A reason this build cannot use leaves 0.7.0's behaviour in place, so say what was there: these two
            // lines are the whole fix for a macOS that has changed what it sends, or that has moved the reason
            // out from under us again. No reason at all is the ordinary case for an AppleScript quit, so it goes
            // in the trace log rather than as a warning — but it is also what reading the wrong slot looks like,
            // so it is not passed over in silence.
            if (eventClass == QuitEventClass && eventId == QuitEventId && reason != QuitAll)
                Logger.TryGet(reason == 0 ? LogEventLevel.Verbose : LogEventLevel.Warning, LogArea.Platform)
                    ?.Log(null, "MacQuitReason: quit reason {Reason}; the quit is asked about as a user's own",
                          reason == 0 ? "none readable" : FourCharCode(reason));
            return false;
        }
        catch (Exception ex)
        {
            // Read from inside a window's Closing: an exception here would take the quit down with it.
            Logger.TryGet(LogEventLevel.Warning, LogArea.Platform)?.Log(null, "MacQuitReason: {Error}", ex.ToString());
            return false;
        }
    }

    /// <summary>A FourCharCode the way AERegistry.h writes it, for the log line that names an unknown one.</summary>
    internal static string FourCharCode(uint code) =>
        new([(char)(byte)(code >> 24), (char)(byte)(code >> 16), (char)(byte)(code >> 8), (char)(byte)code]);
}
