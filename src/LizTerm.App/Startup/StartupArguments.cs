using LizTerm.Core.Session;

namespace LizTerm.App.Startup;

/// <summary>Command line: no argument opens the picker; a saved profile name connects to it; host[:port] or [ipv6][:port] connects ad hoc.</summary>
public sealed record StartupArguments(string? ProfileName, string? Host, int? Port)
{
    public static StartupArguments Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0])) return new StartupArguments(null, null, null);
        var arg = args[0].Trim();

        if (arg.StartsWith('['))
        {
            var close = arg.IndexOf(']');
            if (close > 1)
            {
                var rest = arg[(close + 1)..];
                int? p = rest.StartsWith(':') && int.TryParse(rest[1..], out var parsed) ? parsed : null;
                return new StartupArguments(null, arg[1..close], p);
            }
        }

        var lastColon = arg.LastIndexOf(':');
        if (lastColon > 0 && arg.IndexOf(':') == lastColon && int.TryParse(arg[(lastColon + 1)..], out var port))
            return new StartupArguments(null, arg[..lastColon], port);

        if (arg.Contains('.') || arg.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return new StartupArguments(null, arg, null);

        return new StartupArguments(arg, null, null);
    }

    public SessionProfile? Resolve(IReadOnlyList<SessionProfile> profiles)
    {
        if (ProfileName is not null)
            return profiles.FirstOrDefault(p => p.Name.Equals(ProfileName, StringComparison.OrdinalIgnoreCase));
        if (Host is not null)
            return new SessionProfile { Name = Port is null ? Host : $"{Host}:{Port}", Host = Host, Port = Port ?? 23 };
        return null;
    }
}
