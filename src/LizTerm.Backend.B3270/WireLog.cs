using System.Globalization;

namespace LizTerm.Backend.B3270;

/// <summary>Records every protocol line in both directions. Used for bug reports and as replay fixtures.</summary>
public sealed class WireLog(TextWriter writer) : IDisposable
{
    public const string EnvironmentVariable = "LIZTERM_WIRE_LOG";
    private readonly object _lock = new();

    public static WireLog? FromEnvironment()
    {
        var path = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(path)) return null;
        return new WireLog(new StreamWriter(path, append: true));
    }

    public void Inbound(string line) => Write('<', line);
    public void Outbound(string line) => Write('>', line);

    private void Write(char direction, string line)
    {
        lock (_lock)
        {
            writer.Write(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            writer.Write(' ');
            writer.Write(direction);
            writer.Write(' ');
            writer.WriteLine(line);
            writer.Flush();
        }
    }

    public void Dispose()
    {
        lock (_lock) writer.Dispose();
    }
}
