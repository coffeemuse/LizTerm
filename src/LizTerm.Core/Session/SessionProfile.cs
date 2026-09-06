using System.Text.Json.Serialization;
using LizTerm.Core.Profiles;

namespace LizTerm.Core.Session;

[JsonConverter(typeof(SessionProfileConverter))]
public sealed record SessionProfile
{
    public string Name { get; init; } = "";
    public string Host { get; init; } = "";
    public int Port { get; init; } = 23;
    public bool UseTls { get; init; }
    public bool VerifyCertificate { get; init; } = true;
    /// <summary>The certificate trusted for this host, or null for the engine's default trust. Independent of
    /// <see cref="VerifyCertificate"/>: a pinned profile verifies, against the pin only; verification off ignores
    /// the pin without removing it (spec 3.1).</summary>
    public CertificatePin? PinnedCertificate { get; init; }
    /// <summary>3278/3279 model number, 2 through 5.</summary>
    public int Model { get; init; } = 2;
    public bool Extended { get; init; } = true;
    public string CodePage { get; init; } = "cp037";
    public string? LuName { get; init; }
    /// <summary>When true (the default, as in x3270's and wc3270's own base keymaps and Vista TN3270), the Backspace
    /// key erases the character to the left of the cursor (x3270's Erase action); when false it only moves the
    /// cursor left (BackSpace). The editor writes the field explicitly, so a saved choice survives.</summary>
    public bool DestructiveBackspace { get; init; } = true;
}
