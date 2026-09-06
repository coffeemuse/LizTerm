namespace LizTerm.Core.Session;

/// <summary>A saved connection. Properties are settable rather than init-only on purpose: the System.Text.Json source
/// generator that <c>ProfileJsonContext</c> uses ignores property initializers for init-only members, so a file
/// missing a field would read as the CLR default instead of the value declared here (verified on .NET 10). Nothing
/// mutates a profile; every change goes through a <c>with</c> expression.</summary>
public sealed record SessionProfile
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 23;
    public bool UseTls { get; set; }
    public bool VerifyCertificate { get; set; } = true;
    /// <summary>The certificate trusted for this host, or null for the engine's default trust. Independent of
    /// <see cref="VerifyCertificate"/>: a pinned profile verifies, against the pin only; verification off ignores
    /// the pin without removing it (spec 3.1).</summary>
    public CertificatePin? PinnedCertificate { get; set; }
    /// <summary>3278/3279 model number, 2 through 5.</summary>
    public int Model { get; set; } = 2;
    public bool Extended { get; set; } = true;
    public string CodePage { get; set; } = "cp037";
    public string? LuName { get; set; }
    /// <summary>When true (the default, as in x3270's and wc3270's own base keymaps and Vista TN3270), the Backspace
    /// key erases the character to the left of the cursor (x3270's Erase action); when false it only moves the
    /// cursor left (BackSpace). The editor writes the field explicitly, so a saved choice survives.</summary>
    public bool DestructiveBackspace { get; set; } = true;
}
