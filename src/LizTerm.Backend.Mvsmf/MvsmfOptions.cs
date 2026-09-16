// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Backend.Mvsmf;

/// <param name="BaseUrl">The z/OSMF root, for example <c>http://host:8080/zosmf</c>.</param>
/// <param name="PinnedCertificate">For https: the only certificate accepted. Null means the system's trust.</param>
public sealed record MvsmfOptions(Uri BaseUrl, CertificatePin? PinnedCertificate = null)
{
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
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
            || parsed.Scheme is not ("http" or "https")
            || parsed.Host.Length == 0)
        {
            error = "Enter an http:// or https:// URL.";
            return false;
        }
        if (parsed.Query.Length > 0 || parsed.Fragment.Length > 0)
        {
            error = "The URL cannot have a query or a fragment.";
            return false;
        }
        var path = parsed.AbsolutePath.TrimEnd('/');
        if (path.Length == 0) path = "/zosmf";
        url = new UriBuilder(parsed) { Path = path }.Uri;
        error = null;
        return true;
    }
}
