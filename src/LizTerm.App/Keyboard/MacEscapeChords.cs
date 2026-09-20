// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Runtime.InteropServices;
using Avalonia.Logging;
using LizTerm.App.Menus;
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
/// current event to the view's own keyDown: when it is an Escape key-down without ⌘ and does nothing otherwise, so
/// Avalonia raises the KeyDown it raises on Linux, TerminalScreen maps ⌃⎋ through the keymap, and the Escape press
/// also ends the Ctrl tap that used to survive it (a lone Ctrl release after the lost chord sent Reset or Enter).
/// ⌘⎋ stays swallowed as it always was: the keymap policy refuses ⌘ chords, so on the screen it could only fall
/// through to AvnView's text path and type an ESC character to the host, and in a dialog it would press the cancel
/// button, which Avalonia's Button matches on Key.Escape alone. It is added, never wrapped: a class that already
/// answers cancelOperation: means Avalonia has started handling the chord itself (the right home for this fix), and
/// this code should be revisited rather than run in front of it.
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
    private static IntPtr _nsApplication, _sharedApplication, _currentEvent, _type, _keyCode, _modifierFlags, _keyDown;
    private static readonly object Gate = new();

    // The forwarded keyDown: must never come back here. It does not today, because AvnView answers
    // doCommandBySelector: with nothing; an Avalonia that let NSResponder's default run would route the input
    // context's cancelOperation: straight back, and the guard turns that from a stack overflow into one lost key.
    [ThreadStatic] private static bool _forwarding;

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
                _modifierFlags = sel_registerName("modifierFlags");
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
            ?.Log(null, "MacEscapeChords not installed ({Step}); Ctrl+Escape is left to Avalonia", step);
        return false;
    }

    /// <summary>The rule, on its own so a test can pin it: only an Escape key-down without ⌘ is handed to keyDown:.
    /// cancelOperation: also arrives for ⌘⎋, ⌘. and from code, and none of those is a key the screen should see.</summary>
    internal static bool Forwards(ulong eventType, ushort keyCode, ulong modifierFlags) =>
        eventType == KeyDownEventType && keyCode == EscapeKeyCode && (modifierFlags & MacMenuKeyEquivalents.CommandFlag) == 0;

    private static void Cancel(IntPtr self, IntPtr selector, IntPtr sender)
    {
        if (_forwarding) return;
        try
        {
            _forwarding = true;
            var app = objc_msgSend_IntPtr(_nsApplication, _sharedApplication);
            var theEvent = app == IntPtr.Zero ? IntPtr.Zero : objc_msgSend_IntPtr(app, _currentEvent);
            if (theEvent == IntPtr.Zero) return;
            if (Forwards(objc_msgSend_ulong(theEvent, _type), objc_msgSend_ushort(theEvent, _keyCode), objc_msgSend_ulong(theEvent, _modifierFlags)))
                objc_msgSend_void(self, _keyDown, theEvent);
        }
        catch (Exception ex)
        {
            // Called from AppKit: an exception must not cross back into it. The chord is lost, as it was before.
            Logger.TryGet(LogEventLevel.Warning, LogArea.Platform)?.Log(null, "MacEscapeChords: {Error}", ex.ToString());
        }
        finally
        {
            _forwarding = false;
        }
    }
}
