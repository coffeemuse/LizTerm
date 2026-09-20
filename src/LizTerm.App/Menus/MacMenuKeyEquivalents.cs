// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Runtime.InteropServices;

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
/// class or the method was not found gets a Keys menu without shortcuts, never one that eats keys. The imports
/// are DllImport rather than LibraryImport because LibraryImport's generated stub is unsafe code, which the App
/// project does not use; three of these signatures take a string, so they are not blittable and SystemBellRinger's
/// other reason does not apply.</summary>
internal static class MacMenuKeyEquivalents
{
    private const string LibObjc = "/usr/lib/libobjc.A.dylib";
    private const string MenuClass = "AvnMenu";
    private const string PerformSelector = "performKeyEquivalent:";

    /// <summary>NSEventModifierFlagCommand.</summary>
    internal const ulong CommandFlag = 1UL << 20;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte PerformKeyEquivalent(IntPtr self, IntPtr selector, IntPtr theEvent);

    // Both kept for the life of the process: AppKit holds the replacement's pointer, and the original is the
    // inherited NSMenu implementation the replacement defers to.
    private static readonly PerformKeyEquivalent Replacement = Perform;
    private static PerformKeyEquivalent? _original;
    private static IntPtr _modifierFlags;
    private static readonly object Gate = new();

    public static bool Installed { get; private set; }

    /// <summary>Adds the override once. False off macOS, when libobjc or the class or the method cannot be found,
    /// or when the class already defines the selector itself (an Avalonia that started overriding it would need
    /// this code revisited, not silently wrapped).</summary>
    public static bool Install(bool isMacOS)
    {
        if (!isMacOS) return false;
        lock (Gate)
        {
            if (Installed) return true;
            try
            {
                var cls = objc_getClass(MenuClass);
                if (cls == IntPtr.Zero) return false;
                var selector = sel_registerName(PerformSelector);
                var inherited = class_getInstanceMethod(cls, selector);
                if (inherited == IntPtr.Zero) return false;
                var originalImp = method_getImplementation(inherited);
                if (originalImp == IntPtr.Zero) return false;
                _original = Marshal.GetDelegateForFunctionPointer<PerformKeyEquivalent>(originalImp);
                _modifierFlags = sel_registerName("modifierFlags");
                // BOOL is 'B' on arm64 and 'c' on x86_64; the rest is self, _cmd and the event.
                var encoding = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "B@:@" : "c@:@";
                if (!class_addMethod(cls, selector, Marshal.GetFunctionPointerForDelegate(Replacement), encoding))
                    return false;
                Installed = true;
                return true;
            }
            catch (Exception)
            {
                // Install runs before any window exists; a failure here must degrade to "no shortcuts", never take down launch.
                return false;
            }
        }
    }

    /// <summary>The rule, on its own so a test can pin it: a key-down without ⌘ is never a menu key equivalent.</summary>
    internal static bool Declines(ulong modifierFlags) => (modifierFlags & CommandFlag) == 0;

    private static byte Perform(IntPtr self, IntPtr selector, IntPtr theEvent)
    {
        var flags = theEvent == IntPtr.Zero ? 0UL : objc_msgSend_ulong(theEvent, _modifierFlags);
        if (Declines(flags)) return 0;
        return _original!(self, selector, theEvent);
    }

    [DllImport(LibObjc)] private static extern IntPtr objc_getClass(string name);
    [DllImport(LibObjc)] private static extern IntPtr sel_registerName(string name);
    [DllImport(LibObjc)] private static extern IntPtr class_getInstanceMethod(IntPtr cls, IntPtr selector);
    [DllImport(LibObjc)] private static extern IntPtr method_getImplementation(IntPtr method);
    [DllImport(LibObjc)] [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr imp, string types);
    [DllImport(LibObjc, EntryPoint = "objc_msgSend")] private static extern ulong objc_msgSend_ulong(IntPtr self, IntPtr selector);
}
