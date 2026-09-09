// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Security.Cryptography.X509Certificates;

namespace LizTerm.Core.Security;

/// <summary>The trust anchors the operating system already holds: the macOS keychain, the Windows root store, or
/// the distribution's CA bundle, whichever this machine has. Read once per instance, because the read costs a few
/// hundred milliseconds, the root set does not meaningfully change inside one session, and a restart picks up any
/// change that matters.</summary>
public sealed class SystemTrustAnchors : ITrustAnchorSource
{
    /// <summary>The instance the app uses, so one process reads the store once.</summary>
    public static SystemTrustAnchors Default { get; } = new();

    private readonly Lazy<string?> _pem = new(Read);

    public string? ExportPem() => _pem.Value;

    private static string? Read()
    {
        var roots = ReadStore(StoreName.Root, StoreLocation.LocalMachine);
        roots.AddRange(ReadStore(StoreName.Root, StoreLocation.CurrentUser));
        // A root the OS itself refuses normally stays in Root: Windows records the refusal by putting the
        // certificate in Disallowed as well, and .NET surfaces a macOS "Never Trust" setting through the same
        // store. Handing OpenSSL the Root store unfiltered would make LizTerm trust a CA every other client on
        // the machine rejects, so the refused set is subtracted before the bundle is built.
        var refused = ReadStore(StoreName.Disallowed, StoreLocation.LocalMachine);
        refused.AddRange(ReadStore(StoreName.Disallowed, StoreLocation.CurrentUser));
        try
        {
            var distrusted = Fingerprints(refused);
            // A root too broken to fingerprint is kept here and dropped by Build, which is where one unusable
            // certificate is already handled; it cannot be matched against the refused set either way.
            return TrustAnchorPem.Build([.. roots.Where(root => FingerprintOrNull(root) is not { } f || !distrusted.Contains(f))]);
        }
        catch (Exception)
        {
            // The Lazy field above caches whatever this returns for the process's life, an exception included:
            // Lazy's default mode re-throws a cached exception on every later access. Anything Build and the
            // filter above did not already absorb would otherwise make every connect for the rest of the run
            // throw instead of merely losing that machine's anchors — and fewer anchors is already a recoverable,
            // handled case (ReadStore's own try/catch, and ConnectAsync's fallback to the engine's own default
            // trust), while a permanently poisoned Lazy is not.
            return null;
        }
        finally
        {
            // Each of these wraps a native certificate context; the rest of this codebase disposes certificates
            // rather than leaving a few hundred handles to finalization.
            foreach (var certificate in roots) certificate.Dispose();
            foreach (var certificate in refused) certificate.Dispose();
        }
    }

    private static HashSet<string> Fingerprints(IEnumerable<X509Certificate2> certificates)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var certificate in certificates)
        {
            if (FingerprintOrNull(certificate) is { } fingerprint) set.Add(fingerprint);
        }
        return set;
    }

    /// <summary>The fingerprint, or null for a certificate the platform will enumerate but not hash.</summary>
    private static string? FingerprintOrNull(X509Certificate2 certificate)
    {
        try
        {
            return CertificateReader.Fingerprint(certificate);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Both locations, because a root an administrator installed machine-wide and one the user added
    /// themselves are equally the answer to "what does this machine trust" — and the same holds for a refusal
    /// recorded in either place. A store that cannot be opened contributes nothing rather than failing the
    /// connect: having fewer anchors than hoped is recoverable and already handled, throwing here is not.</summary>
    private static List<X509Certificate2> ReadStore(StoreName name, StoreLocation location)
    {
        try
        {
            using var store = new X509Store(name, location);
            store.Open(OpenFlags.ReadOnly);
            // Materialised before the store closes.
            return [.. store.Certificates];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
