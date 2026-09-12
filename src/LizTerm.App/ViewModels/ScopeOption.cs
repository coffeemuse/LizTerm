// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.ViewModels;

/// <summary>One entry in the session list's scope drop-down. A record rather than a bare string so the filter
/// reads the tag it was given instead of parsing a '#' back off a display label.</summary>
/// <param name="Label">What the drop-down shows: "All sessions", "FAVORITE", or "#PROD".</param>
/// <param name="TagName">The tag this narrows to, or null for every profile.</param>
public sealed record ScopeOption(string Label, string? TagName)
{
    public override string ToString() => Label;
}
