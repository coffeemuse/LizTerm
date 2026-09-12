// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.App.Controls;

/// <summary>One keypad button: the label the user reads and the key the host receives.</summary>
public readonly record struct KeypadKey(string Label, TerminalKey Key);

/// <summary>What the keypad holds, as data (keypad spec §3): three banks of BankSize, which the control lays out as
/// rows at the bottom of the window and as columns on its right. The Keys menu's labels are used where the two
/// overlap ("Field Mark", "SysReq") so the menu and the keypad never disagree; NativeMenuTests holds the menu to a
/// subset of this table.</summary>
public static class KeypadLayout
{
    public const int BankSize = 12;

    /// <summary>PF1 to PF12; PF13 to PF24; then the issue's list plus Erase Input, which nothing else binds, and
    /// Dup and Field Mark, which joined the Keys menu after the issue was written. Twelve in the third bank is what
    /// keeps the grid rectangular both ways.</summary>
    public static IReadOnlyList<IReadOnlyList<KeypadKey>> Banks { get; } =
    [
        [.. Enumerable.Range(0, BankSize).Select(i => new KeypadKey($"PF{i + 1}", TerminalKey.PF1 + i))],
        [.. Enumerable.Range(0, BankSize).Select(i => new KeypadKey($"PF{i + 13}", TerminalKey.PF13 + i))],
        [
            new("PA1", TerminalKey.PA1), new("PA2", TerminalKey.PA2), new("PA3", TerminalKey.PA3),
            new("Enter", TerminalKey.Enter), new("Clear", TerminalKey.Clear), new("Reset", TerminalKey.Reset),
            new("Attn", TerminalKey.Attn), new("SysReq", TerminalKey.SysReq), new("Erase EOF", TerminalKey.EraseEof),
            new("Erase Input", TerminalKey.EraseInput), new("Dup", TerminalKey.Dup), new("Field Mark", TerminalKey.FieldMark),
        ],
    ];
}
