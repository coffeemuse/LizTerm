namespace LizTerm.Core.Security;

/// <summary>What a host presented in a TLS handshake (spec 5.1). <paramref name="Pem"/> is every certificate in the
/// chain, leaf first; <paramref name="Pinnable"/> says whether an engine trusting only those certificates would
/// accept the leaf, and <paramref name="NotPinnableReason"/> says why not.</summary>
public sealed record PresentedCertificate(string Sha256, string Subject, string Pem, bool Pinnable, string? NotPinnableReason);
