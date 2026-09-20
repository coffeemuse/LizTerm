// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Runtime.InteropServices;
using Avalonia.Logging;
using LizTerm.App.Platform;
using static LizTerm.App.Platform.LibObjc;

namespace LizTerm.App.Keyboard;

/// <summary>Gives the screen the Escape chord AppKit keeps from it, so Ctrl+Escape sends Clear on macOS (#157).
/// NSWindow.sendEvent: does not deliver an Escape key-down with ⌃ or ⌘ to the first responder's keyDown:. It sends
/// the responder cancelOperation: instead, and where nothing answers that, doCommandBySelector:cancel:. Avalonia's
/// AvnView raises its KeyDown only from keyDown: and answers doCommandBySelector: with nothing, so the chord was
/// lost before Avalonia saw it, while ⇧⎋, ⌥⎋ and a bare ⎋ take the keyDown: route and always worked. Measured on
/// 2026-09-20 with a bare NSView in a probe process, event by event; the same probe showed a cancelOperation: added
/// to the view class receiving the chord with the key-down as NSApp's currentEvent.
///
/// Install adds that cancelOperation: to AvnView, the class of every Avalonia window's content view. It hands the
/// current event to the view's own keyDown: when it is an Escape key-down with ⌃ and without ⌘ (Forwards is the
/// pure rule), so Avalonia raises the KeyDown it raises on Linux, TerminalScreen maps ⌃⎋ through the keymap, and
/// the Escape press also ends the Ctrl tap that used to survive it (a lone Ctrl release after the lost chord sent
/// Reset or Enter). Anything else (⌘⎋, ⌘., a programmatic send) goes on to the next responder that answers the
/// selector, which is where the message went before this view answered it; nothing in Avalonia does today, so
/// those are swallowed as they always were. ⌘⎋ is not forwarded on purpose: the keymap policy refuses ⌘ chords, so
/// on the screen it could only fall through to AvnView's text path and type an ESC character to the host, and in a
/// dialog it would press the cancel button, which Avalonia's Button matches on Key.Escape alone. Only ⌃ is
/// forwarded because only ⌃ is diverted: a bare, ⇧ or ⌥ Escape reaches cancelOperation: solely through the input
/// context's doCommandBySelector:, after keyDown: has raised it once, and forwarding it from there would raise it
/// twice. The method is added, never wrapped: a class that already answers cancelOperation: means Avalonia has
/// started handling the chord itself (the right home for this fix), and this code should be revisited rather than
/// run in front of it.
///
/// Installed is what SessionWindow.EscapeChordsReachScreen reads: where the method could not be added, the Keys
/// menu shows Clear without a Ctrl+Escape shortcut it cannot deliver, a warning in the trace log names the step,
/// and the menu and the keypad still send Clear.</summary>
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
    private static readonly MacOverride Override = new(nameof(MacEscapeChords), "Ctrl+Escape is left to Avalonia");
    private static IntPtr _nsApplication, _sharedApplication, _currentEvent, _type, _keyCode, _modifierFlags, _keyDown,
        _nextResponder, _respondsToSelector;

    // Everything here runs on AppKit's main thread. The forwarded keyDown: must never come back here: it does not
    // today, because AvnView answers doCommandBySelector: with nothing, and under an Avalonia that let NSResponder's
    // default run (which would route the input context's cancelOperation: straight back) the flag ends the loop
    // after the one KeyDown the forward has already raised.
    private static bool _forwarding;

    public static bool Installed => Override.Installed;

    /// <summary>Adds the method once. False off macOS, when libobjc or the class cannot be found, or when the
    /// class already answers the selector.</summary>
    public static bool Install(bool isMacOS) => Override.Install(isMacOS, Add);

    /// <summary>The steps in order; the first that fails is the answer, and null means the method is in place.</summary>
    private static string? Add()
    {
        var cls = objc_getClass(ViewClass);
        if (cls == IntPtr.Zero) return $"no {ViewClass} class";
        var selector = sel_registerName(CancelSelector);
        if (class_getInstanceMethod(cls, selector) != IntPtr.Zero) return $"{ViewClass} already answers {CancelSelector}";
        _nsApplication = objc_getClass("NSApplication");
        if (_nsApplication == IntPtr.Zero) return "no NSApplication class";
        _sharedApplication = sel_registerName("sharedApplication");
        _currentEvent = sel_registerName("currentEvent");
        _type = sel_registerName("type");
        _keyCode = sel_registerName("keyCode");
        _modifierFlags = sel_registerName("modifierFlags");
        _keyDown = sel_registerName("keyDown:");
        _nextResponder = sel_registerName("nextResponder");
        _respondsToSelector = sel_registerName("respondsToSelector:");
        // A void return, then self, _cmd and the sender.
        if (!class_addMethod(cls, selector, Marshal.GetFunctionPointerForDelegate(Added), "v@:@"))
            return $"{CancelSelector} could not be added to {ViewClass}";
        return null;
    }

    /// <summary>The rule, on its own so a test can pin it: only an Escape key-down with ⌃ and without ⌘ is handed
    /// to keyDown:, the one shape AppKit diverts that the screen should see. cancelOperation: also arrives for ⌘⎋,
    /// ⌘. and from code, and for a bare, ⇧ or ⌥ Escape keyDown: has already run.</summary>
    internal static bool Forwards(ulong eventType, ushort keyCode, ulong modifierFlags) =>
        eventType == KeyDownEventType && keyCode == EscapeKeyCode
        && (modifierFlags & NSEvent.ControlFlag) != 0 && (modifierFlags & NSEvent.CommandFlag) == 0;

    private static void Cancel(IntPtr self, IntPtr selector, IntPtr sender)
    {
        if (_forwarding) return;
        try
        {
            _forwarding = true;
            var app = objc_msgSend_IntPtr(_nsApplication, _sharedApplication);
            var theEvent = app == IntPtr.Zero ? IntPtr.Zero : objc_msgSend_IntPtr(app, _currentEvent);
            if (theEvent != IntPtr.Zero
                && Forwards(objc_msgSend_ulong(theEvent, _type), objc_msgSend_ushort(theEvent, _keyCode), objc_msgSend_ulong(theEvent, _modifierFlags)))
            {
                objc_msgSend_void(self, _keyDown, theEvent);
                return;
            }
            // Not the chord: on up the responder chain to the first responder that answers, as the message went
            // before this view answered it.
            for (var responder = objc_msgSend_IntPtr(self, _nextResponder); responder != IntPtr.Zero; responder = objc_msgSend_IntPtr(responder, _nextResponder))
            {
                if (!objc_msgSend_bool(responder, _respondsToSelector, selector)) continue;
                objc_msgSend_void(responder, selector, sender);
                return;
            }
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
