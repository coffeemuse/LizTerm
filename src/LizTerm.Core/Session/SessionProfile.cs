namespace LizTerm.Core.Session;

public sealed record SessionProfile
{
    public string Name { get; init; } = "";
    public string Host { get; init; } = "";
    public int Port { get; init; } = 23;
    public bool UseTls { get; init; }
    public bool VerifyCertificate { get; init; } = true;
    /// <summary>3278/3279 model number, 2 through 5.</summary>
    public int Model { get; init; } = 2;
    public bool Extended { get; init; } = true;
    public string CodePage { get; init; } = "cp037";
    public string? LuName { get; init; }
    /// <summary>When true, the Backspace key erases the character to the left of the cursor (x3270's Erase
    /// action) instead of only moving the cursor left (BackSpace), which is the x3270-family default.</summary>
    public bool DestructiveBackspace { get; init; }
}
