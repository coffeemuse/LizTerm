// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Backend.B3270;
using LizTerm.Backend.B3270.Process;
using LizTerm.Core.Session;

namespace LizTerm.Integration.Tests;

/// <summary>The one place the bundled-engine gate lives, so the skip-versus-fail contract cannot drift between the
/// tests that depend on it: without a bundled engine a test skips, under LIZTERM_REQUIRE_ENGINE it fails, and a
/// binary that is present but did not resolve always fails (see <see cref="EngineRequirement"/>).</summary>
internal static class BundledEngine
{
    /// <summary>The bundled engine, or no return at all: this skips or fails the calling test. LIZTERM_B3270_PATH
    /// is ignored, so a Homebrew b3270 cannot stand in for the one CI built.</summary>
    public static B3270Location Require()
    {
        B3270Location? location = null;
        string? missing = null;
        try
        {
            location = B3270Locator.Find(overridePath: null, AppContext.BaseDirectory);
        }
        catch (BackendUnavailableException e)
        {
            missing = e.Message;
        }

        // Find throws the same exception type for "nothing anywhere" and "there but not executable", so ask the
        // locator where it looked: a candidate that exists on disk means the second, which must never skip.
        var present = B3270Locator.Candidates(overridePath: null, AppContext.BaseDirectory).Any(c => File.Exists(c.Path));
        var outcome = EngineRequirement.Decide(location is not null, present, Environment.GetEnvironmentVariable(EngineRequirement.Variable));
        if (outcome == EngineRequirementOutcome.Fail) Assert.Fail(missing ?? "no bundled engine");
        Assert.SkipWhen(outcome == EngineRequirementOutcome.Skip, missing ?? "no bundled engine");
        return location!;
    }
}
