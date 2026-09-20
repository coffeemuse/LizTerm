// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Runtime.InteropServices;
using LizTerm.App.Platform;
using static LizTerm.App.Platform.LibObjc;

namespace LizTerm.App.Menus;

/// <summary>Keeps a native menu key equivalent from stealing a 3270 keystroke (Keys menu shortcuts spec §3.1).
/// On macOS a NativeMenuItem gesture is a real AppKit key equivalent, and NSApplication.sendEvent: offers every
/// key-down to the main menu's performKeyEquivalent: before the key window's responder chain, so a Keys item
/// with Gesture Shift+F1 would send PF13 through the menu and TerminalScreen would never see the key
/// (native menus spec §2.1, measured again on 2026-09-19). Install adds a performKeyEquivalent: to Avalonia's
/// AvnMenu class, the class of every main menu it builds, that answers NO for a key-down without ⌘ and hands the
/// rest to NSMenu's own implementation. So Edit's ⌘C and Window's ⌘M keep working and ⇧F1 falls through to the
/// screen, while AppKit still draws the shortcut column. The rule holds because no native item outside Keys
/// carries a ⌘-less gesture; NativeMenuTests pins that.
///
/// Installed is what SessionWindow.ApplyKeymap checks before giving a Keys item a gesture: a process where the
/// class or the method was not found gets a Keys menu without shortcuts, never one that eats keys, and a warning
/// in the trace log naming the step that failed, so the missing column is not a silent one. The once-only attempt
/// and that warning are Platform/MacOverride's; the runtime imports are Platform/LibObjc's.</summary>
internal static class MacMenuKeyEquivalents
{
    private const string MenuClass = "AvnMenu";
    private const string PerformSelector = "performKeyEquivalent:";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte PerformKeyEquivalent(IntPtr self, IntPtr selector, IntPtr theEvent);

    // Both kept for the life of the process: AppKit holds the replacement's pointer, and the original is the
    // inherited NSMenu implementation the replacement defers to.
    private static readonly PerformKeyEquivalent Replacement = Perform;
    private static PerformKeyEquivalent? _original;
    private static IntPtr _modifierFlags;
    private static readonly MacOverride Override = new(nameof(MacMenuKeyEquivalents), "the Keys menu shows no shortcuts");

    public static bool Installed => Override.Installed;

    /// <summary>Adds the override once. False off macOS, when libobjc or the class or the method cannot be found,
    /// or when the class already defines the selector itself (an Avalonia that started overriding it would need
    /// this code revisited, not silently wrapped).</summary>
    public static bool Install(bool isMacOS) => Override.Install(isMacOS, Add);

    /// <summary>The steps in order; the first that fails is the answer, and null means the override is in place.</summary>
    private static string? Add()
    {
        var cls = objc_getClass(MenuClass);
        if (cls == IntPtr.Zero) return $"no {MenuClass} class";
        var selector = sel_registerName(PerformSelector);
        var inherited = class_getInstanceMethod(cls, selector);
        if (inherited == IntPtr.Zero) return $"{MenuClass} inherits no {PerformSelector}";
        var originalImp = method_getImplementation(inherited);
        if (originalImp == IntPtr.Zero) return $"{PerformSelector} has no implementation";
        _original = Marshal.GetDelegateForFunctionPointer<PerformKeyEquivalent>(originalImp);
        _modifierFlags = sel_registerName("modifierFlags");
        // A BOOL return, then self, _cmd and the event.
        var encoding = BoolEncoding + "@:@";
        if (!class_addMethod(cls, selector, Marshal.GetFunctionPointerForDelegate(Replacement), encoding))
            return $"{MenuClass} already defines {PerformSelector}";
        return null;
    }

    /// <summary>The rule, on its own so a test can pin it: a key-down without ⌘ is never a menu key equivalent.</summary>
    internal static bool Declines(ulong modifierFlags) => (modifierFlags & NSEvent.CommandFlag) == 0;

    private static byte Perform(IntPtr self, IntPtr selector, IntPtr theEvent)
    {
        var flags = theEvent == IntPtr.Zero ? 0UL : objc_msgSend_ulong(theEvent, _modifierFlags);
        if (Declines(flags)) return 0;
        return _original!(self, selector, theEvent);
    }
}
