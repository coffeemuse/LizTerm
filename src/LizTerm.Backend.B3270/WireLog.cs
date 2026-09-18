// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;

namespace LizTerm.Backend.B3270;

/// <summary>Records every protocol line in both directions. Used for bug reports and as replay fixtures.</summary>
public sealed class WireLog(TextWriter writer, string? path = null) : IDisposable
{
    public const string EnvironmentVariable = "LIZTERM_WIRE_LOG";
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>Opens <paramref name="path"/> for appending. Throws <see cref="IOException"/> (a missing
    /// directory included) or <see cref="UnauthorizedAccessException"/> when it cannot.</summary>
    public WireLog(string path) : this(Open(path), path)
    {
    }

    /// <summary>Owner-only on Unix. This file holds every keystroke and every screen the host painted — the most
    /// sensitive thing LizTerm writes — and the default 0644 left it readable by every other account on the
    /// machine, while the CA file, which holds nothing but public certificates, was already owner-only (#139).
    /// The mode applies when the file is created; appending to one that already exists leaves its mode alone,
    /// which is correct — that is the user's file to chmod. Windows inherits the directory's ACL as before.</summary>
    private static StreamWriter Open(string path)
    {
        var options = new FileStreamOptions { Mode = FileMode.Append, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new StreamWriter(path, options);
    }

    /// <summary>The file being written, when known.</summary>
    public string? Path { get; } = path;

    /// <summary>The log named by <see cref="EnvironmentVariable"/>, or null. <paramref name="error"/> is set only
    /// when the variable names a path that could not be opened, so the caller can say so once instead of
    /// silently running without a log.</summary>
    public static WireLog? TryFromEnvironment(out string? error)
    {
        error = null;
        var path = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            return new WireLog(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = ex.Message;
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
