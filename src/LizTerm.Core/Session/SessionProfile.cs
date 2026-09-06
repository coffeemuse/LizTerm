namespace LizTerm.Core.Session;

/// <summary>A saved connection. Positional, with a default on every parameter, on purpose: the System.Text.Json
/// source generator that <c>ProfileJsonContext</c> uses honours constructor defaults for a field missing from a
/// file, where it ignores the initializer of an init-only property and reads the CLR default instead (verified on
/// .NET 10). Object initializers and <c>with</c> expressions work as before, and the properties stay init-only, so
/// the one instance a session, the picker, and the store share cannot be changed under any of them.</summary>
/// <param name="PinnedCertificate">The certificate trusted for this host, or null for the engine's default trust.
/// Independent of <paramref name="VerifyCertificate"/>: a pinned profile verifies, against the pin only;
/// verification off ignores the pin without removing it (spec 3.1).</param>
/// <param name="Model">3278/3279 model number, 2 through 5.</param>
/// <param name="DestructiveBackspace">When true (the default, as in x3270's and wc3270's own base keymaps and Vista
/// TN3270), the Backspace key erases the character to the left of the cursor (x3270's Erase action); when false it
/// only moves the cursor left (BackSpace). The editor writes the field explicitly, so a saved choice survives.</param>
public sealed record SessionProfile(
    string Name = "",
    string Host = "",
    int Port = 23,
    bool UseTls = false,
    bool VerifyCertificate = true,
    CertificatePin? PinnedCertificate = null,
    int Model = 2,
    bool Extended = true,
    string CodePage = "cp037",
    string? LuName = null,
    bool DestructiveBackspace = true);
