// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Session;

/// <summary>The one spelling of a profile's 3270 terminal type. The backend puts it on b3270's -model command
/// line and the status bar shows it to the user; both read it from here so the engine argument and the status
/// bar cannot drift apart, and so App never has to name the backend to render it.</summary>
public static class TerminalType
{
    /// <summary>For example <c>3279-2-E</c>. The 3279 family is colour and the 3278 family mono; the -E suffix is
    /// the extended data stream, valid on both. "Model 2" on its own is ambiguous in a way this is not.</summary>
    public static string For(SessionProfile profile) =>
        $"{(profile.Display == TerminalDisplay.Mono ? "3278" : "3279")}-{profile.Model}{(profile.Extended ? "-E" : "")}";
}
