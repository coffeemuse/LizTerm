// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Updates;

/// <summary>One of three outcomes a release check can have — the StartupPlan shape: a closed set matched with a
/// switch, rather than a nullable ReleaseInfo and a separate error string.</summary>
public abstract record UpdateCheckResult
{
    public sealed record UpToDate : UpdateCheckResult;
    public sealed record NewerAvailable(string Version, string HtmlUrl) : UpdateCheckResult;
    public sealed record Failed(string Reason) : UpdateCheckResult;
}
