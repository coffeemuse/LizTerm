// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.App.Startup;

/// <summary>What the app opens once the splash has closed. Pure so it can be tested without windows.</summary>
public abstract record StartupPlan
{
    public sealed record ShowError(string Message) : StartupPlan;
    /// <param name="FromStore">True when the profile is a saved one, whose certificate choice can be written back.</param>
    public sealed record OpenSession(SessionProfile Profile, bool FromStore) : StartupPlan;
    public sealed record OpenPicker : StartupPlan;

    public static StartupPlan Decide(string? backendError, StartupArguments arguments, IReadOnlyList<SessionProfile> profiles)
    {
        if (backendError is not null) return new ShowError(backendError);
        var profile = arguments.Resolve(profiles);
        if (profile is null) return new OpenPicker();
        // Resolve hands back the saved instance itself when the argument named one, which is what makes the
        // certificate choice writable back to it; anything else is the ad hoc profile it just built.
        return new OpenSession(profile, FromStore: profiles.Any(p => ReferenceEquals(p, profile)));
    }
}
