// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Backend.Mvsmf;

public sealed record MvsmfOptions
{
    /// <param name="baseUrl">The z/OSMF root, for example <c>http://host:8080/zosmf</c>. Kept as given.</param>
    /// <param name="pinnedCertificate">For https: the only certificate accepted. Null means the system's trust.</param>
    /// <exception cref="ArgumentException">The URL is not an absolute http or https URL, or carries a userid,
    /// a password, a query or a fragment.</exception>
    public MvsmfOptions(Uri baseUrl, CertificatePin? pinnedCertificate = null)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        if (CheckBaseUrl(baseUrl) is { } error) throw new ArgumentException(error, nameof(baseUrl));
        BaseUrl = baseUrl;
        PinnedCertificate = pinnedCertificate;
    }

    /// <summary>The z/OSMF root, for example <c>http://host:8080/zosmf</c>.</summary>
    public Uri BaseUrl { get; }

    /// <summary>For https: the only certificate accepted. Null means the system's trust.</summary>
    public CertificatePin? PinnedCertificate { get; }

    /// <summary>Reads what a user typed. An empty path becomes <c>/zosmf</c> and a trailing slash is dropped; the
    /// scheme is honoured, never changed.</summary>
    public static bool TryNormalizeBaseUrl(string? text, out Uri? url, out string? error)
    {
        url = null;
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0)
        {
            error = "Enter the mvsMF URL.";
            return false;
        }
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
        {
            error = NotHttp;
            return false;
        }
        error = CheckBaseUrl(parsed);
        if (error is not null) return false;
        var path = parsed.AbsolutePath.TrimEnd('/');
        if (path.Length == 0) path = "/zosmf";
        url = new UriBuilder(parsed) { Path = path }.Uri;
        return true;
    }

    private const string NotHttp = "Enter an http:// or https:// URL.";

    /// <summary>The rules a base URL must meet, shared by the constructor and <see cref="TryNormalizeBaseUrl"/>.
    /// Returns what is wrong, or null.</summary>
    private static string? CheckBaseUrl(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || url.Host.Length == 0) return NotHttp;
        if (url.UserInfo.Length > 0) return "Leave the userid and password out of the URL.";
        if (url.Query.Length > 0 || url.Fragment.Length > 0) return "The URL cannot have a query or a fragment.";
        return null;
    }
}
