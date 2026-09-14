// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Files;

/// <summary>The one spelling of "open a web page, else name it". IUriOpener's contract is that false means the caller
/// shows the URL instead, and an opener or a URL that throws is the same failure. Help's links and the update
/// dialog's Download both come through here, so the rule and its wording cannot drift apart.</summary>
public static class LinkOpening
{
    /// <summary>True only when the platform opened <paramref name="url"/>: false for no opener, for a refusal, and
    /// for anything that throws on the way. Never throws.</summary>
    public static async Task<bool> TryOpenAsync(IUriOpener? opener, string url)
    {
        try
        {
            return opener is not null && await opener.OpenAsync(new Uri(url));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>What to show when <see cref="TryOpenAsync"/> answers false.</summary>
    public static string NotOpened(string url) => $"Could not open a browser. The page is at {url}.";
}
