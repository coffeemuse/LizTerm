// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Files;

/// <summary>Turning a profile name into something safe to put in a filename. One spelling, because two callers
/// build names from the same profile name — the wire log and a screen capture — and a profile name is free
/// text that can hold a path separator.</summary>
public static class SafeFileName
{
    /// <summary>ASCII letters and digits plus <c>. _ -</c> survive; everything else becomes an underscore.</summary>
    public static string Of(string name) =>
        new(name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').ToArray());
}
