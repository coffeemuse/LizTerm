using LizTerm.Core.Session;

namespace LizTerm.App.Startup;

/// <summary>Command line: no argument opens the picker; a saved profile name connects to it; x3270's ad hoc host
/// syntax <c>[L:][Y:][lu@]host[:port]</c> (IPv6 hosts bracketed) connects without a profile. <c>L:</c> is TLS,
/// <c>Y:</c> turns certificate verification off, and the LU part is passed to the engine verbatim, comma lists
/// included. A syntax error sets <see cref="Error"/> to <see cref="Usage"/> and resolves to the picker.</summary>
public sealed record StartupArguments(
    string? ProfileName,
    string? Host,
    int? Port,
    bool UseTls = false,
    bool VerifyCertificate = true,
    string? LuName = null,
    string? Error = null)
{
    public const string Usage = "Usage: LizTerm [profile | [L:][Y:][lu@]host[:port] | [L:][Y:][lu@][ipv6][:port]]";

    private static readonly StartupArguments Invalid = new(null, null, null, Error: Usage);

    public static StartupArguments Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0])) return new StartupArguments(null, null, null);
        var arg = args[0].Trim();

        var tls = false;
        var verify = true;
        var sawPrefix = false;
        while (arg.Length >= 2 && char.IsAsciiLetter(arg[0]) && arg[1] == ':')
        {
            switch (char.ToUpperInvariant(arg[0]))
            {
                case 'L' when !tls: tls = true; break;
                case 'Y' when verify: verify = false; break;
                default: return Invalid;
            }
            sawPrefix = true;
            arg = arg[2..];
        }

        string? lu = null;
        var at = arg.IndexOf('@');
        if (at >= 0)
        {
            if (at == 0) return Invalid;
            lu = arg[..at];
            arg = arg[(at + 1)..];
        }

        var (host, port) = ParseHostPort(arg, sawPrefix || lu is not null);
        if (host is null) return sawPrefix || lu is not null ? Invalid : new StartupArguments(arg, null, null);
        return new StartupArguments(null, host, port, tls, verify, lu);
    }

    /// <summary>Host and optional port. Without a prefix or LU, a bare word with no dot is a profile name, so this
    /// returns a null host for it; with one, any non-empty word is a host.</summary>
    private static (string? Host, int? Port) ParseHostPort(string arg, bool forceHost)
    {
        if (arg.Length == 0) return (null, null);
        if (arg.StartsWith('['))
        {
            var close = arg.IndexOf(']');
            if (close > 1)
            {
                var rest = arg[(close + 1)..];
                int? p = rest.StartsWith(':') && int.TryParse(rest[1..], out var parsed) ? parsed : null;
                return (arg[1..close], p);
            }
        }

        var lastColon = arg.LastIndexOf(':');
        if (lastColon > 0 && arg.IndexOf(':') == lastColon && int.TryParse(arg[(lastColon + 1)..], out var port))
            return (arg[..lastColon], port);

        if (forceHost || arg.Contains('.') || arg.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return (arg, null);

        return (null, null);
    }

    public SessionProfile? Resolve(IReadOnlyList<SessionProfile> profiles)
    {
        if (Error is not null) return null;
        if (ProfileName is not null)
            return profiles.FirstOrDefault(p => p.Name.Equals(ProfileName, StringComparison.OrdinalIgnoreCase));
        if (Host is null) return null;
        var port = Port ?? (UseTls ? 992 : 23);
        var address = $"{Host}:{port}";
        return new SessionProfile
        {
            Name = LuName is null ? address : $"{LuName}@{address}",
            Host = Host,
            Port = port,
            UseTls = UseTls,
            VerifyCertificate = VerifyCertificate,
            LuName = LuName,
        };
    }
}
