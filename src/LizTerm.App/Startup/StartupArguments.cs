using System.Globalization;
using LizTerm.Core.Session;

namespace LizTerm.App.Startup;

/// <summary>Command line: no argument opens the picker; a saved profile name connects to it; x3270's ad hoc host
/// syntax <c>[L:][Y:][lu@]host[:port]</c> (IPv6 hosts bracketed) connects without a profile. <c>L:</c> is TLS,
/// <c>Y:</c> turns certificate verification off, and the LU part is passed to the engine verbatim, comma lists
/// included. A syntax error sets <see cref="Error"/> to <see cref="Usage"/> and resolves to the picker.
/// <para>The two readings overlap — <c>CONS01@mvs</c> and <c>a:b</c> are legal profile names as well as legal ad
/// hoc hosts — so <see cref="Parse"/> does not choose between them. It records the argument as typed in
/// <see cref="Argument"/> and, when the text also reads as a host, the ad hoc fields beside it;
/// <see cref="Resolve"/> is the one that has the saved list and picks.</para></summary>
public sealed record StartupArguments(
    string? Argument,
    string? Host,
    int? Port,
    bool UseTls = false,
    bool VerifyCertificate = true,
    string? LuName = null,
    string? Error = null)
{
    public const string Usage = "Usage: LizTerm [profile | [L:][Y:][lu@]host[:port] | [L:][Y:][lu@][ipv6][:port]]";

    /// <summary>A malformed argument still carries its text, so it can name a saved profile even when it is not a
    /// legal host: "a:b" is a usage error as a host and a perfectly good profile name.</summary>
    private static StartupArguments Invalid(string argument) => new(argument, null, null, Error: Usage);

    public static StartupArguments Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0])) return new StartupArguments(null, null, null);
        var argument = args[0].Trim();
        var arg = argument;

        var tls = false;
        var verify = true;
        var sawPrefix = false;
        while (arg.Length >= 2 && char.IsAsciiLetter(arg[0]) && arg[1] == ':' && CouldBeHost(arg[2..]))
        {
            switch (char.ToUpperInvariant(arg[0]))
            {
                case 'L' when !tls: tls = true; break;
                case 'Y' when verify: verify = false; break;
                default: return Invalid(argument);
            }
            sawPrefix = true;
            arg = arg[2..];
        }

        string? lu = null;
        var at = arg.IndexOf('@');
        if (at >= 0)
        {
            if (at == 0) return Invalid(argument);
            lu = arg[..at];
            arg = arg[(at + 1)..];
        }

        var forceHost = sawPrefix || lu is not null;
        var (host, port) = ParseHostPort(arg, forceHost, out var malformed);
        if (malformed) return Invalid(argument);
        if (host is null) return forceHost ? Invalid(argument) : new StartupArguments(argument, null, null);
        return new StartupArguments(argument, host, port, tls, verify, lu);
    }

    /// <summary>Whether what follows a <c>&lt;letter&gt;:</c> head could be a host, which is what makes that head a
    /// prefix at all. An all-digit remainder cannot be one, so <c>l:3270</c> is the one-letter host <c>l</c> on port
    /// 3270 rather than TLS to a host called <c>3270</c>.</summary>
    private static bool CouldBeHost(string rest) => rest.Length > 0 && !rest.All(char.IsAsciiDigit);

    /// <summary>A port as x3270 writes one: plain digits in range. Culture-independent and without
    /// <see cref="NumberStyles"/>' default tolerance for surrounding space and a leading sign, so "+3270",
    /// " 3270", "-1", "0" and "99999" are all rejected rather than reaching the engine.</summary>
    private static bool TryParsePort(string text, out int port) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535;

    /// <summary>Host and optional port; a null host means the text does not name one.
    /// <paramref name="malformed"/> is set when the text carries a port section that is not a port
    /// ("mvs.local:abc", "mvs.local:99999"). That is a usage error, not a hostname that happens to contain a
    /// colon, which is what it used to become — silently connecting somewhere the user never named.</summary>
    private static (string? Host, int? Port) ParseHostPort(string arg, bool forceHost, out bool malformed)
    {
        malformed = false;
        if (arg.Length == 0) return (null, null);
        if (arg.StartsWith('['))
        {
            var close = arg.IndexOf(']');
            if (close > 1)
            {
                var rest = arg[(close + 1)..];
                if (rest.Length == 0) return (arg[1..close], null);
                if (rest.StartsWith(':') && TryParsePort(rest[1..], out var bracketed)) return (arg[1..close], bracketed);
                malformed = true;
                return (null, null);
            }
        }

        var lastColon = arg.LastIndexOf(':');
        if (lastColon > 0 && arg.IndexOf(':') == lastColon)
        {
            if (TryParsePort(arg[(lastColon + 1)..], out var port)) return (arg[..lastColon], port);
            malformed = true;
            return (null, null);
        }

        if (forceHost || arg.Contains('.') || arg.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return (arg, null);

        return (null, null);
    }

    /// <summary>The saved profile the argument names, else the ad hoc profile it describes, else null. An exact
    /// name match always wins: the ad hoc forms overlap legal profile names, and only this call has the list that
    /// tells them apart, so a profile called "CONS01@tk5" stays reachable by its own name.</summary>
    public SessionProfile? Resolve(IReadOnlyList<SessionProfile> profiles)
    {
        if (Argument is { } argument)
        {
            var saved = profiles.FirstOrDefault(p => p.Name.Equals(argument, StringComparison.OrdinalIgnoreCase));
            if (saved is not null) return saved;
        }
        if (Error is not null || Host is null) return null;
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
