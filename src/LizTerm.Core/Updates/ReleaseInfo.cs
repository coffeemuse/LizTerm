// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Updates;

/// <summary>One published release, as far as a caller needs to know: a plain version string (no "v" prefix —
/// GitHubReleaseChecker strips it) and the page to send someone to.</summary>
public sealed record ReleaseInfo(string Version, string HtmlUrl);
