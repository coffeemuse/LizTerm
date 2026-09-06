namespace LizTerm.Core.Session;

/// <summary>One-shot choices for a single connect attempt. A null field means "as the profile says". A non-null
/// <paramref name="Pin"/> is verified against instead of the profile's pin; it is ignored when the effective
/// verify setting is off (spec 3.1).</summary>
public sealed record ConnectOptions(bool? VerifyCertificate = null, CertificatePin? Pin = null);
