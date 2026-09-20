// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Runtime.InteropServices;
using LizTerm.Core.Settings;

namespace LizTerm.App.Bell;

/// <summary>The platform's own alert sound, and the App's P/Invoke for sounds; the other is
/// `Menus/MacMenuKeyEquivalents`. NSBeep and MessageBeep play the sound
/// the user chose at the alert volume they chose, and stay silent when they have turned interface sounds off — a
/// bundled WAV would override all three (#47). Neither needs a file, which matters for a single-file publish.
/// Linux has no guaranteed audio path without a library dependency, so CanRing says no there and Ring does nothing;
/// the Preferences window disables the radio and says why. The imports are DllImport rather than LibraryImport on
/// purpose: the signatures are blittable, and LibraryImport's generated stub is unsafe code the App otherwise has
/// no need of.</summary>
public sealed class SystemBellRinger : IBellRinger
{
    private const uint MB_OK = 0;

    public bool CanRing(BellSound sound) =>
        sound == BellSound.SystemAlert && (OperatingSystem.IsMacOS() || OperatingSystem.IsWindows());

    public void Ring(BellSound sound)
    {
        if (!CanRing(sound)) return;
        if (OperatingSystem.IsMacOS()) NSBeep();
        else MessageBeep(MB_OK);
    }

    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit", EntryPoint = "NSBeep")]
    private static extern void NSBeep();

    [DllImport("user32.dll", EntryPoint = "MessageBeep")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MessageBeep(uint uType);
}
