// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Profiles;

/// <summary>What one Manage Tags action did — enough to say it exactly: "Renamed on 3 of 5 profiles. Could not
/// write gateway: ...". Never compared whole; <see cref="Changed"/> is a list.</summary>
/// <param name="Carriers">Profiles that carried the tag when the action started.</param>
/// <param name="Changed">Profile names written, in the order they were written.</param>
/// <param name="FailedProfile">The profile whose write failed, or null.</param>
/// <param name="Error">The failure's message, or null on success. Non-null with a null
/// <paramref name="FailedProfile"/> means tags.json was not written: before any profile when
/// <paramref name="Changed"/> is short of <paramref name="Carriers"/>, else after every one.</param>
/// <param name="After">The profiles and registry as the action left them on disk, so a caller can redraw without
/// reading every file again; null when the action had nothing to do.</param>
public sealed record TagChangeResult(int Carriers, IReadOnlyList<string> Changed, string? FailedProfile, string? Error,
    TagSnapshot? After = null)
{
    /// <summary>An action that had nothing to do and wrote nothing.</summary>
    public static TagChangeResult Nothing { get; } = new(0, [], null, null);

    public bool Succeeded => Error is null;
}
