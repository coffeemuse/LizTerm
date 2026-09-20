// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Runtime.InteropServices;

namespace LizTerm.App.Platform;

/// <summary>The Objective-C runtime calls LizTerm's two macOS overrides share (Menus/MacMenuKeyEquivalents and
/// Keyboard/MacEscapeChords). DllImport rather than LibraryImport because LibraryImport's generated stub is unsafe
/// code, which the App project does not use; the signatures that take a string are not blittable, so
/// SystemBellRinger's other reason does not apply. Every objc_msgSend variant is the same symbol under a name that
/// says what it returns and takes, since the runtime's one function is called with the callee's own signature.</summary>
internal static class LibObjc
{
    private const string Path = "/usr/lib/libobjc.A.dylib";

    /// <summary>The type encoding of a BOOL return: 'B' on arm64 and 'c' on x86_64.</summary>
    internal static string BoolEncoding => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "B" : "c";

    [DllImport(Path)] internal static extern IntPtr objc_getClass(string name);
    [DllImport(Path)] internal static extern IntPtr sel_registerName(string name);
    [DllImport(Path)] internal static extern IntPtr class_getInstanceMethod(IntPtr cls, IntPtr selector);
    [DllImport(Path)] internal static extern IntPtr method_getImplementation(IntPtr method);
    [DllImport(Path)] [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr imp, string types);
    [DllImport(Path, EntryPoint = "objc_msgSend")] internal static extern IntPtr objc_msgSend_IntPtr(IntPtr self, IntPtr selector);
    [DllImport(Path, EntryPoint = "objc_msgSend")] internal static extern ulong objc_msgSend_ulong(IntPtr self, IntPtr selector);
    [DllImport(Path, EntryPoint = "objc_msgSend")] internal static extern ushort objc_msgSend_ushort(IntPtr self, IntPtr selector);
    [DllImport(Path, EntryPoint = "objc_msgSend")] internal static extern void objc_msgSend_void(IntPtr self, IntPtr selector, IntPtr arg);
}
