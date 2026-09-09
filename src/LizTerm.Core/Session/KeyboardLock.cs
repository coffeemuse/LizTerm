// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Session;

public enum KeyboardLock
{
    Unlocked,
    NotConnected,
    WaitingForHost,
    TerminalWait,
    Deferred,
    MinusFunction,
    ProtectedField,
    NumericOnly,
    Overflow,
    Dbcs,
    Scrolled,
    Disabled,
    FieldWait,
    FileTransfer,
    Unknown,
}
