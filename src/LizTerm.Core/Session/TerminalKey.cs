// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Session;

public enum TerminalKey
{
    Enter,
    Clear,
    PF1, PF2, PF3, PF4, PF5, PF6, PF7, PF8, PF9, PF10, PF11, PF12,
    PF13, PF14, PF15, PF16, PF17, PF18, PF19, PF20, PF21, PF22, PF23, PF24,
    PA1, PA2, PA3,
    Attn,
    SysReq,
    Reset,
    Tab,
    BackTab,
    Home,
    /// <summary>Moves the cursor to the end of what is already typed in the current field; no 3270 keyboard
    /// had this key, but every emulator since offers it (#177).</summary>
    FieldEnd,
    EraseEof,
    EraseInput,
    Delete,
    /// <summary>Non-destructive: moves the cursor left one position.</summary>
    Backspace,
    /// <summary>Destructive backspace: erases the character to the left of the cursor.</summary>
    Erase,
    Insert,
    Dup,
    FieldMark,
    Newline,
    Up,
    Down,
    Left,
    Right,
}
