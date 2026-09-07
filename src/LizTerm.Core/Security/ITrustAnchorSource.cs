namespace LizTerm.Core.Security;

/// <summary>Where the trust anchors an emulator engine should verify against come from. A statically linked engine
/// carries whatever OpenSSL directory was compiled into it, which is a path on the build machine and usually
/// nothing on the user's, so the anchors have to be supplied rather than assumed (spec 1).</summary>
public interface ITrustAnchorSource
{
    /// <summary>The anchors as a PEM, or null when there are none. Null rather than an empty string: an empty CA
    /// file makes b3270 fail the connect outright, so "nothing to offer" must stay distinguishable from "here is
    /// an empty list" all the way to the caller (spec 2, fact 5).</summary>
    string? ExportPem();
}

/// <summary>A source with no anchors, which leaves the engine on its own default trust. The backend's default, so
/// nothing silently depends on the machine's store unless a caller asked for it.</summary>
public sealed class NoTrustAnchors : ITrustAnchorSource
{
    public static readonly NoTrustAnchors Instance = new();
    public string? ExportPem() => null;
}
