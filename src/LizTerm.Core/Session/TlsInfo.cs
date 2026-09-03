namespace LizTerm.Core.Session;

public sealed record TlsInfo(bool Secure, bool? Verified, string? SessionInfo, string? HostCertificate);
