// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Platform;

/// <summary>The NSEvent modifier flags the macOS overrides read (Menus/MacMenuKeyEquivalents and
/// Keyboard/MacEscapeChords) and their tests compose chords from, numbered as AppKit's NSEventModifierFlags are.
/// Beside LibObjc because they are AppKit's, not either override's.</summary>
internal static class NSEvent
{
    /// <summary>NSEventModifierFlagShift.</summary>
    internal const ulong ShiftFlag = 1UL << 17;

    /// <summary>NSEventModifierFlagControl.</summary>
    internal const ulong ControlFlag = 1UL << 18;

    /// <summary>NSEventModifierFlagOption.</summary>
    internal const ulong OptionFlag = 1UL << 19;

    /// <summary>NSEventModifierFlagCommand.</summary>
    internal const ulong CommandFlag = 1UL << 20;

    /// <summary>NSEventModifierFlagFunction.</summary>
    internal const ulong FunctionFlag = 1UL << 23;
}
