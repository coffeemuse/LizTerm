// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Backend.B3270.Tests;

/// <summary>Tests that set a process-wide environment variable (LIZTERM_WIRE_LOG here) must not run beside tests that read it (spec 8).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EnvironmentCollection
{
    public const string Name = "Environment";
}
