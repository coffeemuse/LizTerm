// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Integration.Tests;

public enum EngineRequirementOutcome
{
    Run,
    Skip,
    Fail,
}

/// <summary>The engine smoke test's gate, kept pure so its three outcomes are asserted without spawning anything:
/// a bundled engine runs the test; none skips it, unless LIZTERM_REQUIRE_ENGINE holds any non-blank value, which
/// turns the skip into a failure so a CI job that just built the engine cannot go green by skipping.
/// A binary that is <em>present</em> but did not resolve — the locator's not-executable arm, which is what a
/// downloaded CI artifact looks like before <c>chmod +x</c> — always fails: "there and unusable" is a broken
/// engine, not an absent one, and skipping it is the same green-by-skipping hole this gate exists to close.</summary>
public static class EngineRequirement
{
    public const string Variable = "LIZTERM_REQUIRE_ENGINE";

    public static EngineRequirementOutcome Decide(bool found, bool present, string? requireVariable)
    {
        if (found) return EngineRequirementOutcome.Run;
        if (present) return EngineRequirementOutcome.Fail;
        return string.IsNullOrWhiteSpace(requireVariable) ? EngineRequirementOutcome.Skip : EngineRequirementOutcome.Fail;
    }
}
