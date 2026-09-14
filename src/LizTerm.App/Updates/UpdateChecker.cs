// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Net.Http;
using System.Text.Json;
using LizTerm.Core.Updates;

namespace LizTerm.App.Updates;

public static class UpdateChecker
{
    public static async Task<UpdateCheckResult> CheckAsync(IReleaseChecker checker, string currentVersion, CancellationToken cancellationToken)
    {
        // This build's own version is checked first and named: a version that will not compare is this copy's fault,
        // not GitHub's data, and asking GitHub cannot fix it.
        if (!ReleaseVersion.IsValid(currentVersion))
            return new UpdateCheckResult.Failed($"This build's version, {currentVersion}, cannot be compared with a release.");
        try
        {
            var latest = await checker.GetLatestReleaseAsync(cancellationToken);
            return ReleaseVersion.IsNewer(latest.Version, currentVersion)
                ? new UpdateCheckResult.NewerAvailable(latest.Version, ReleasePage(latest.HtmlUrl))
                : new UpdateCheckResult.UpToDate();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException)
        {
            return new UpdateCheckResult.Failed(FriendlyReason(ex));
        }
    }

    /// <summary>The page Download opens. The platform launcher opens whatever it is handed — a file: URL, a custom
    /// scheme — so only a page under this project's releases is taken from the response; anything else gets the
    /// releases index.</summary>
    private static string ReleasePage(string htmlUrl) =>
        Uri.TryCreate(htmlUrl, UriKind.Absolute, out var page) && page.AbsoluteUri.StartsWith(ProjectLinks.Releases + "/", StringComparison.Ordinal)
            ? page.AbsoluteUri
            : ProjectLinks.Releases;

    /// <summary>Never the exception's own message: OpenLinkAsync's "fall through and say so" shape, not a new
    /// error-reporting idiom. A status means GitHub or a proxy did answer, so it is never blamed on the network.</summary>
    private static string FriendlyReason(Exception ex) => ex switch
    {
        TaskCanceledException => "The request timed out.",
        HttpRequestException { HttpRequestError: HttpRequestError.ProxyTunnelError }
            or HttpRequestException { StatusCode: HttpStatusCode.ProxyAuthenticationRequired } => "The proxy refused the connection to GitHub.",
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests } =>
            "GitHub is limiting requests from this network. Try again later.",
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } => "GitHub has no published release to compare with.",
        HttpRequestException { StatusCode: { } status } => $"GitHub answered with an error (HTTP {(int)status}).",
        HttpRequestException => "Could not reach GitHub.",
        _ => "The release information could not be read.",
    };
}
