using System.Globalization;

namespace LizTerm.Backend.B3270;

/// <summary>Records every protocol line in both directions. Used for bug reports and as replay fixtures.</summary>
public sealed class WireLog(TextWriter writer) : IDisposable
{
    public const string EnvironmentVariable = "LIZTERM_WIRE_LOG";
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>Set by <see cref="FromEnvironment"/> when the environment variable names a path
    /// that could not be opened for writing, so the caller can surface it instead of silently
    /// running without a wire log. Cleared on a subsequent successful open.</summary>
    public static string? LastOpenError { get; private set; }

    public static WireLog? FromEnvironment()
    {
        var path = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var log = new WireLog(new StreamWriter(path, append: true));
            LastOpenError = null;
            return log;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LastOpenError = ex.Message;
            return null;
        }
    }

    public void Inbound(string line) => Write('<', line);
    public void Outbound(string line) => Write('>', line);

    private void Write(char direction, string line)
    {
        lock (_lock)
        {
            if (_disposed) return;
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
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            writer.Dispose();
        }
    }
}
