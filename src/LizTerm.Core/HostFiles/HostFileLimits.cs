// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

/// <summary>Sizes the host cannot take, checked here before a request is made, since the host writes what fits and
/// only then fails.</summary>
public static class HostFileLimits
{
    /// <summary>The most a UNIX file can hold on the tested host: measured stored and read back intact, and under
    /// the host's request-body ceiling. The backend's compatibility log records where this comes from; raise it
    /// when the host grows.</summary>
    public const long MaxUnixFileBytes = 1_048_576;
}
