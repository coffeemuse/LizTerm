namespace LizTerm.Core.Session;

/// <summary>The host connection could not be established; Lines is the emulator's explanation.</summary>
public sealed class ConnectionFailedException(IReadOnlyList<string> lines)
    : Exception(string.Join(" ", lines))
{
    public IReadOnlyList<string> Lines { get; } = lines;
}

/// <summary>An emulator action was rejected (for example, a key while the keyboard is locked).</summary>
public sealed class EmulatorActionException(string message) : Exception(message);

/// <summary>The emulator engine is missing, not executable, or too old.</summary>
public sealed class BackendUnavailableException(string message) : Exception(message);
