namespace LizTerm.Core.Session;

/// <summary>One-shot choices for a single connect attempt. A null field means "as the profile says".</summary>
public sealed record ConnectOptions(bool? VerifyCertificate = null);
