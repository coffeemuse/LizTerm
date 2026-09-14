// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net.Http;
using System.Text.Json;
using LizTerm.Core.Updates;

namespace LizTerm.App.Updates;

public static class UpdateChecker
{
    public static async Task<UpdateCheckResult> CheckAsync(IReleaseChecker checker, string currentVersion, CancellationToken cancellationToken)
    {
        try
        {
            var latest = await checker.GetLatestReleaseAsync(cancellationToken);
            return ReleaseVersion.IsNewer(latest.Version, currentVersion)
                ? new UpdateCheckResult.NewerAvailable(latest.Version, latest.HtmlUrl)
                : new UpdateCheckResult.UpToDate();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException)
        {
            return new UpdateCheckResult.Failed(FriendlyReason(ex));
        }
    }

    /// <summary>Never the exception's own message: OpenLinkAsync's "fall through and say so" shape, not a new
    /// error-reporting idiom.</summary>
    private static string FriendlyReason(Exception ex) => ex switch
    {
        TaskCanceledException => "The request timed out.",
        HttpRequestException => "Could not reach GitHub.",
        _ => "The release information could not be read.",
    };
}
