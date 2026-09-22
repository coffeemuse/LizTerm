// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Integration.Tests;

/// <summary>Tests that set a process-wide environment variable (the locale variables in EngineLocaleTests) must not
/// run beside tests that spawn an engine which would inherit it.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EnvironmentCollection
{
    public const string Name = "Environment";
}
