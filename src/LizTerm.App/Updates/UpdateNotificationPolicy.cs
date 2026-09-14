// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Updates;

/// <summary>Whether an *automatic* check is allowed to interrupt the user. A manual check ignores this and always
/// shows its result — see App.CheckForUpdatesManuallyAsync.</summary>
public static class UpdateNotificationPolicy
{
    public static bool ShouldShowAutomatically(UpdateCheckResult result, string? skippedVersion) =>
        result is UpdateCheckResult.NewerAvailable newer && newer.Version != skippedVersion;
}
