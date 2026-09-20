// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Runtime.InteropServices;
using Avalonia.Logging;
using static LizTerm.App.Platform.LibObjc;

namespace LizTerm.App.Keyboard;

/// <summary>Gives the screen the Escape chords AppKit keeps from it, so Ctrl+Escape sends Clear on macOS (#157).
/// NSWindow.sendEvent: does not deliver an Escape key-down with ⌃ or ⌘ to the first responder's keyDown:. It sends
/// the responder cancelOperation: instead, and where nothing answers that, doCommandBySelector:cancel:. Avalonia's
/// AvnView raises its KeyDown only from keyDown: and answers doCommandBySelector: with nothing, so the chord was
/// lost before Avalonia saw it, while ⇧⎋, ⌥⎋ and a bare ⎋ take the keyDown: route and always worked. Measured on
/// 2026-09-20 with a bare NSView in a probe process, event by event; the same probe showed a cancelOperation: added
/// to the view class receiving the chord with the key-down as NSApp's currentEvent.
///
/// Install adds that cancelOperation: to AvnView, the class of every Avalonia window's content view. It hands the
/// current event to the view's own keyDown: when it is the Escape key-down and does nothing otherwise, so Avalonia
/// raises the KeyDown it raises on Windows and Linux, TerminalScreen maps ⌃⎋ through the keymap, and the Escape
/// press also ends the Ctrl tap that used to survive it (a lone Ctrl release after the lost chord sent Reset or
/// Enter). It is added, never wrapped: a class that already answers cancelOperation: means Avalonia has started
/// handling the chord itself, and this code should be revisited rather than run in front of it.
///
/// A process where the class or the selector cannot be set up gets a warning in the trace log naming the step, and
/// keeps the old behaviour: the menu and the keypad still send Clear.</summary>
internal static class MacEscapeChords
{
    private const string ViewClass = "AvnView";
    private const string CancelSelector = "cancelOperation:";

    /// <summary>NSEventTypeKeyDown.</summary>
    internal const ulong KeyDownEventType = 10;

    /// <summary>kVK_Escape, the virtual key code of the Escape key on every Apple keyboard.</summary>
    internal const ushort EscapeKeyCode = 53;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CancelOperation(IntPtr self, IntPtr selector, IntPtr sender);

    // Kept for the life of the process: AppKit holds the pointer.
    private static readonly CancelOperation Added = Cancel;
    private static IntPtr _nsApplication, _sharedApplication, _currentEvent, _type, _keyCode, _keyDown;
    private static readonly object Gate = new();

    public static bool Installed { get; private set; }

    /// <summary>Adds the method once. False off macOS, when libobjc or the class cannot be found, or when the
    /// class already answers the selector.</summary>
    public static bool Install(bool isMacOS)
    {
        if (!isMacOS) return false;
        lock (Gate)
        {
            if (Installed) return true;
            try
            {
                var cls = objc_getClass(ViewClass);
                if (cls == IntPtr.Zero) return Failed($"no {ViewClass} class");
                var selector = sel_registerName(CancelSelector);
                if (class_getInstanceMethod(cls, selector) != IntPtr.Zero)
                    return Failed($"{ViewClass} already answers {CancelSelector}");
                _nsApplication = objc_getClass("NSApplication");
                if (_nsApplication == IntPtr.Zero) return Failed("no NSApplication class");
                _sharedApplication = sel_registerName("sharedApplication");
                _currentEvent = sel_registerName("currentEvent");
                _type = sel_registerName("type");
                _keyCode = sel_registerName("keyCode");
                _keyDown = sel_registerName("keyDown:");
                // A void return, then self, _cmd and the sender.
                if (!class_addMethod(cls, selector, Marshal.GetFunctionPointerForDelegate(Added), "v@:@"))
                    return Failed($"{CancelSelector} could not be added to {ViewClass}");
                Installed = true;
                return true;
            }
            catch (Exception ex)
            {
                // Install runs before any window exists; a failure here must degrade to the old behaviour, never take down launch.
                return Failed(ex.ToString());
            }
        }
    }

    /// <summary>The one place every failure path goes through: the step is logged where Program's LogToTrace
    /// sends it, and the answer is false.</summary>
    private static bool Failed(string step)
    {
        Logger.TryGet(LogEventLevel.Warning, LogArea.Platform)
            ?.Log(null, "MacEscapeChords not installed ({Step}); Ctrl+Escape does not reach the screen", step);
        return false;
    }

    /// <summary>The rule, on its own so a test can pin it: only the Escape key-down that AppKit diverted is handed
    /// to keyDown:. cancelOperation: also arrives for ⌘. and from code, and neither is a key the screen should see.</summary>
    internal static bool Forwards(ulong eventType, ushort keyCode) => eventType == KeyDownEventType && keyCode == EscapeKeyCode;

    private static void Cancel(IntPtr self, IntPtr selector, IntPtr sender)
    {
        try
        {
            var app = objc_msgSend_IntPtr(_nsApplication, _sharedApplication);
            var theEvent = app == IntPtr.Zero ? IntPtr.Zero : objc_msgSend_IntPtr(app, _currentEvent);
            if (theEvent == IntPtr.Zero) return;
            if (Forwards(objc_msgSend_ulong(theEvent, _type), objc_msgSend_ushort(theEvent, _keyCode)))
                objc_msgSend_void(self, _keyDown, theEvent);
        }
        catch (Exception ex)
        {
            // Called from AppKit: an exception must not cross back into it. The chord is lost, as it was before.
            Logger.TryGet(LogEventLevel.Warning, LogArea.Platform)?.Log(null, "MacEscapeChords: {Error}", ex.ToString());
        }
    }
}
