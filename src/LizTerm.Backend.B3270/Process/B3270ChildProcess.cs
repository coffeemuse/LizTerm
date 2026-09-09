// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Process;

public sealed class B3270ChildProcess(string executablePath) : IB3270Process
{
    private const int StderrTailSize = 50;
    private readonly ConcurrentQueue<string> _stderr = new();
    private System.Diagnostics.Process? _process;

    public TextReader StandardOutput => _process?.StandardOutput ?? throw NotStarted();
    public TextWriter StandardInput => _process?.StandardInput ?? throw NotStarted();
    public IReadOnlyList<string> StderrTail => _stderr.ToArray();

    public void Start(IReadOnlyList<string> arguments)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var psi = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);
        try
        {
            _process = System.Diagnostics.Process.Start(psi) ?? throw new BackendUnavailableException($"Could not start {executablePath}");
        }
        catch (Exception ex) when (ex is not BackendUnavailableException)
        {
            throw new BackendUnavailableException($"Could not start {executablePath}: {ex.Message}");
        }
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            _stderr.Enqueue(e.Data);
            while (_stderr.Count > StderrTailSize) _stderr.TryDequeue(out string? _);
        };
        _process.BeginErrorReadLine();
        _process.StandardInput.AutoFlush = true;
        _process.StandardInput.NewLine = "\n";
    }

    public async Task<int> WaitForExitAsync()
    {
        if (_process is null) throw NotStarted();
        await _process.WaitForExitAsync();
        return _process.ExitCode;
    }

    public void Kill()
    {
        // Killing a process on the way out must never throw: InvalidOperationException means it
        // already exited, but a Win32Exception (or other platform failure) can also escape here.
        try { _process?.Kill(entireProcessTree: true); }
        catch (Exception) { /* already exited, or the platform refused; nothing more we can do */ }
    }

    public void Dispose()
    {
        Kill();
        _process?.Dispose();
    }

    private static InvalidOperationException NotStarted() => new("Process not started");
}
