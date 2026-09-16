// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Core.Profiles;

/// <summary>Which certificate pin a profile save keeps. The profile editor is handed a copy of the profile and
/// may hold it open across a pin written from a session window, so its idea of the pin can be older than the
/// file's; but it also drops a pin on purpose when the host or port is repointed, and Forget clears one
/// outright. Those three are all expressed as "the editor has no pin", so they are separated here rather than
/// guessed at by the caller.</summary>
public static class PinMerge
{
    /// <param name="edited">What the editor produced. It never invents a pin, so a non-null one here is the copy
    /// it loaded — possibly older than the file's.</param>
    /// <param name="onDisk">The profile as it is on disk now, read under its *original* name (a rename deletes
    /// the old file only after the save, so a rename still finds it), or null when there is no file at all: a
    /// profile that has never been saved.</param>
    /// <param name="pinCleared">The user pressed Forget. Distinguishes a deliberate clear from an editor copy
    /// that simply predates the pin.</param>
    public static CertificatePin? Resolve(SessionProfile edited, SessionProfile? onDisk, bool pinCleared)
    {
        if (pinCleared) return null;
        if (onDisk?.PinnedCertificate is not { } stored) return edited.PinnedCertificate;

        // A pin belongs to the host and port it was taken from (see SessionProfile). Repointing either
        // invalidates it, and that is the editor's own rule -- carrying the stored pin forward regardless would
        // resurrect a pin the user dropped by pointing the profile somewhere else.
        var samePlace = string.Equals(edited.Host, onDisk.Host, StringComparison.OrdinalIgnoreCase)
                        && edited.Port == onDisk.Port;
        return samePlace ? stored : null;
    }

    /// <summary>The REST pin's twin of <see cref="Resolve"/>: the same three cases, keyed on the REST URL instead of
    /// the host and port. URLs compare ignoring case and a trailing slash, the two differences the editor's own
    /// normalisation can leave.</summary>
    public static CertificatePin? ResolveHostFiles(SessionProfile edited, SessionProfile? onDisk, bool pinCleared)
    {
        if (pinCleared) return null;
        if (onDisk?.HostFilesPinnedCertificate is not { } stored) return edited.HostFilesPinnedCertificate;
        return SameUrl(edited.HostFilesUrl, onDisk.HostFilesUrl) ? stored : null;
    }

    /// <summary>Both merges applied to what the editor produced: the one call every editor save site makes.</summary>
    public static SessionProfile Apply(SessionProfile edited, SessionProfile? onDisk, bool pinCleared, bool hostFilesPinCleared) =>
        edited with
        {
            PinnedCertificate = Resolve(edited, onDisk, pinCleared),
            HostFilesPinnedCertificate = ResolveHostFiles(edited, onDisk, hostFilesPinCleared),
        };

    /// <summary>The one rule for "the same REST URL": ignoring case, surrounding blanks and a trailing slash. The
    /// profile editor normalises both URLs first and then asks this.</summary>
    public static bool SameUrl(string? first, string? second) =>
        string.Equals(first?.Trim().TrimEnd('/'), second?.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
