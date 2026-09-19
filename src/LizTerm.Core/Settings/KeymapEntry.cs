// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Settings;

/// <summary>One binding's value in keymap.json, as strings: Core knows neither Avalonia's key names nor the 3270
/// keys (LizTerm.App reads a SendKey's name into TerminalKey). SendKey names a 3270 key, TypeText is text to type,
/// and Unbound takes the chord away from the default (editable keymap spec §3.1).</summary>
public abstract record KeymapEntry
{
    private KeymapEntry() { }

    public sealed record SendKey(string KeyName) : KeymapEntry;

    public sealed record TypeText(string Text) : KeymapEntry;

    public sealed record Unbound : KeymapEntry
    {
        public static readonly Unbound Instance = new();
    }
}
