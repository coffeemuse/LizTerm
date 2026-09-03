using System.Text;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Protocol;

/// <summary>Builds x3270 host syntax: [prefix:...][lu@]host[:port]. L: = TLS tunnel.</summary>
public static class HostStringBuilder
{
    public static string Build(SessionProfile profile)
    {
        var sb = new StringBuilder();
        if (profile.UseTls) sb.Append("L:");
        if (!string.IsNullOrWhiteSpace(profile.LuName)) sb.Append(profile.LuName.Trim()).Append('@');
        var host = profile.Host.Trim();
        sb.Append(host.Contains(':') ? $"[{host}]" : host);
        sb.Append(':').Append(profile.Port);
        return sb.ToString();
    }

    public static string ModelArgument(SessionProfile profile) =>
        $"3279-{profile.Model}{(profile.Extended ? "-E" : "")}";
}
