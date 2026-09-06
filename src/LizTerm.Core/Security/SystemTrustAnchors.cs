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
        try
        {
            return TrustAnchorPem.Build([.. ReadStore(StoreLocation.LocalMachine), .. ReadStore(StoreLocation.CurrentUser)]);
        }
        catch (Exception)
        {
            // The Lazy field below caches whatever this returns for the process's life, an exception included:
            // Lazy's default mode re-throws a cached exception on every later access. One malformed certificate
            // in the store (Fingerprint or ExportCertificatePem inside TrustAnchorPem.Build) would otherwise make
            // every connect for the rest of the run throw instead of merely losing that machine's anchors — and
            // fewer anchors is already a recoverable, handled case (ReadStore's own try/catch, and ConnectAsync's
            // fallback to the engine's own default trust), while a permanently poisoned Lazy is not.
            return null;
        }
    }

    /// <summary>Both locations, because a root an administrator installed machine-wide and one the user added
    /// themselves are equally the answer to "what does this machine trust". A store that cannot be opened
    /// contributes nothing rather than failing the connect: having fewer anchors than hoped is recoverable and
    /// already handled, throwing here is not.</summary>
    private static List<X509Certificate2> ReadStore(StoreLocation location)
    {
        try
        {
            using var store = new X509Store(StoreName.Root, location);
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
