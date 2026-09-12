// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Settings;

/// <summary>What the bell sounds like. An enum rather than a bool so a user-chosen sound file can join later as one
/// more member and one more path field, with no change to the existing ones (bell spec §2.1). Written to the
/// settings file by name; a member an older build does not know falls to None for that key alone.</summary>
public enum BellSound
{
    None,
    /// <summary>The platform's own alert sound: NSBeep on macOS, MessageBeep on Windows, nothing on Linux.</summary>
    SystemAlert,
}
