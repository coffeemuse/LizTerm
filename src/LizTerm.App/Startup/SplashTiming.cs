// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Startup;

/// <summary>When the splash closes: at the first click or key, but never before <see cref="Minimum"/> after it
/// was shown, and at <see cref="Maximum"/> with no input at all (spec 7).</summary>
public sealed class SplashTiming(TimeSpan minimum, TimeSpan maximum)
{
    public static readonly SplashTiming Default = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2.5));

    public TimeSpan Minimum { get; } = minimum;
    public TimeSpan Maximum { get; } = maximum;

    /// <summary>How long from now until the splash should close, given how long it has already been shown and
    /// when (on the same elapsed scale) a dismiss was asked for. Both are monotonic elapsed times, so a system
    /// clock step cannot make the splash skip its minimum or overstay its maximum.</summary>
    public TimeSpan CloseAfter(TimeSpan shownFor, TimeSpan? dismissRequestedAt)
    {
        if (dismissRequestedAt is not { } requested) return Maximum - shownFor;
        return (requested > Minimum ? requested : Minimum) - shownFor;
    }
}
