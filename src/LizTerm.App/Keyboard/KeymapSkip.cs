// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Keyboard;

/// <summary>Why one entry of keymap.json is not in force (#168). The entry itself is kept and written back
/// untouched; this is only what the Keyboard tab tells the user, so a typo can be found without diffing the file
/// against the documentation.</summary>
public enum KeymapSkipReason
{
    /// <summary>The chord does not parse: a key name this build does not know, or a modifier it will not take.</summary>
    UnknownChord,

    /// <summary>The chord parses, but it names a 3270 key this build does not have.</summary>
    UnknownKey,

    /// <summary>A text binding with nothing to type.</summary>
    EmptyText,

    /// <summary>The value is neither a key name nor a text binding — a number, a list, an object of another shape.</summary>
    WrongShape,
}

/// <summary>One skipped entry, by the spelling the file uses, so the user can search for it there.</summary>
/// <param name="KeyName">The 3270 key name the file gave, for <see cref="KeymapSkipReason.UnknownKey"/> only.</param>
public sealed record KeymapSkip(string Chord, KeymapSkipReason Reason, string? KeyName = null);
