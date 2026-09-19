// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Diagnostics.CodeAnalysis;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Keyboard;

/// <summary>What a chord does once the file's strings are read (editable keymap spec §3.2): send a 3270 key, type
/// text, or nothing, the chord having been taken away from the default. The typed twin of KeymapEntry.</summary>
public abstract record KeymapAction
{
    private KeymapAction() { }

    public sealed record SendKey(TerminalKey Key) : KeymapAction;

    public sealed record TypeText(string Text) : KeymapAction;

    public sealed record Unbound : KeymapAction
    {
        public static readonly Unbound Instance = new();
    }

    /// <summary>Reads a file entry. False for a 3270 key name this build does not know (a newer build's, or a
    /// typo; the caller keeps the entry verbatim) and for empty text.</summary>
    public static bool TryFrom(KeymapEntry entry, [NotNullWhen(true)] out KeymapAction? action)
    {
        action = entry switch
        {
            KeymapEntry.SendKey send when TryKey(send.KeyName, out var key) => new SendKey(key),
            KeymapEntry.TypeText type when type.Text.Length > 0 => new TypeText(type.Text),
            KeymapEntry.Unbound => Unbound.Instance,
            _ => null,
        };
        return action is not null;
    }

    public KeymapEntry ToEntry() => this switch
    {
        SendKey send => new KeymapEntry.SendKey(send.Key.ToString()),
        TypeText type => new KeymapEntry.TypeText(type.Text),
        _ => KeymapEntry.Unbound.Instance,
    };

    /// <summary>By name only: Enum.TryParse would also accept "3", which no one wrote on purpose.</summary>
    private static bool TryKey(string name, out TerminalKey key)
    {
        key = default;
        return name.Length > 0 && !char.IsDigit(name[0])
               && Enum.TryParse(name, ignoreCase: true, out key) && Enum.IsDefined(key);
    }
}
