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
/// <paramref name="FailedProfile"/> means every profile was written and tags.json was not.</param>
public sealed record TagChangeResult(int Carriers, IReadOnlyList<string> Changed, string? FailedProfile, string? Error)
{
    /// <summary>An action that had nothing to do and wrote nothing.</summary>
    public static TagChangeResult Nothing { get; } = new(0, [], null, null);

    public bool Succeeded => Error is null;
}
