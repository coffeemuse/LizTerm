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
    /// the extended data stream (<see cref="IsExtended"/>), valid on both. "Model 2" on its own is ambiguous in a way
    /// this is not. b3270 4.5 reads only the model number and colour from its -model, so the suffix there changes
    /// nothing; the backend turns the stream off by name (#200).</summary>
    public static string For(SessionProfile profile) =>
        $"{(profile.Display == TerminalDisplay.Mono ? "3278" : "3279")}-{profile.Model}{(IsExtended(profile) ? "-E" : "")}";

    /// <summary>Whether the session runs with the extended data stream: the profile's Extended, unless it has a
    /// custom size. b3270 drops an oversize without the stream, so a custom size keeps it on, as the profile editor's
    /// greyed box shows; a profile saved before #200 can carry both, and its custom size must not shrink to the
    /// model's own. An oversize the engine would refuse counts as one, since the command line refuses it by
    /// name.</summary>
    public static bool IsExtended(SessionProfile profile) =>
        profile.Extended || !OversizeGeometry.TryFor(profile, out var oversize, out _) || oversize is not null;
}
