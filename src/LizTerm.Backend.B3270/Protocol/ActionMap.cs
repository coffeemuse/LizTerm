// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Protocol;

public sealed record B3270Action(string Name, params string[] Args);

public static class ActionMap
{
    public static B3270Action ForKey(TerminalKey key)
    {
        if (key >= TerminalKey.PF1 && key <= TerminalKey.PF24)
            return new B3270Action("PF", (key - TerminalKey.PF1 + 1).ToString());
        if (key >= TerminalKey.PA1 && key <= TerminalKey.PA3)
            return new B3270Action("PA", (key - TerminalKey.PA1 + 1).ToString());
        return key switch
        {
            TerminalKey.Enter => new("Enter"),
            TerminalKey.Clear => new("Clear"),
            TerminalKey.Attn => new("Attn"),
            TerminalKey.SysReq => new("SysReq"),
            TerminalKey.Reset => new("Reset"),
            TerminalKey.Tab => new("Tab"),
            TerminalKey.BackTab => new("BackTab"),
            TerminalKey.Home => new("Home"),
            TerminalKey.FieldEnd => new("FieldEnd"),
            TerminalKey.EraseEof => new("EraseEOF"),
            TerminalKey.EraseInput => new("EraseInput"),
            TerminalKey.Delete => new("Delete"),
            TerminalKey.Backspace => new("BackSpace"),
            TerminalKey.Erase => new("Erase"),
            TerminalKey.Insert => new("ToggleInsert"),
            TerminalKey.Dup => new("Dup"),
            TerminalKey.FieldMark => new("FieldMark"),
            TerminalKey.Newline => new("Newline"),
            TerminalKey.Up => new("Up"),
            TerminalKey.Down => new("Down"),
            TerminalKey.Left => new("Left"),
            TerminalKey.Right => new("Right"),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "No b3270 action for key"),
        };
    }
}
