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
        ConfigureLocale(psi.Environment);
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

    /// <summary>The engine must print numbers the way JSON reads them, whatever the desktop's language (#170).
    /// b3270 4.5ga6 calls <c>setlocale(LC_ALL, "")</c> at start-up on every platform but Windows
    /// (<c>Common/codepage.c</c>) and then prints every JSON double with <c>%g</c> (<c>Common/json.c</c>), so under
    /// a locale whose decimal separator is a comma — Croatian, German, French and most of Europe — a run that took
    /// a millisecond or more is answered with <c>"time":0,039</c>. That is not JSON: the parser drops the line, the
    /// run is never answered, and a Connect that succeeded in every visible way still times out thirty seconds
    /// later. Upstream master still formats the same way. The fix is the child's environment: <c>LC_NUMERIC=C</c>,
    /// which pins the decimal point and nothing else, so the engine keeps reading the codeset from the user's own
    /// locale. <c>LC_ALL</c> outranks every <c>LC_*</c> variable, so a value there is spelled out into the
    /// categories it stood for and the variable itself removed; an empty <c>LC_ALL</c> is unset by POSIX's rule and
    /// stands for nothing. A Mac launched from the Finder carries no locale variables at all, which is why the
    /// report came from a Linux desktop. Windows engines never call setlocale, and the variables cost nothing there.
    /// Internal so a test can hand it a dictionary; <see cref="Start"/> hands it the real environment.</summary>
    internal static void ConfigureLocale(IDictionary<string, string?> environment)
    {
        if (environment.TryGetValue("LC_ALL", out var all) && !string.IsNullOrEmpty(all))
        {
            foreach (var category in LocaleCategories) environment[category] = all;
        }
        environment.Remove("LC_ALL");
        environment["LC_NUMERIC"] = "C";
    }

    /// <summary>What <c>LC_ALL</c> stands for, less <c>LC_NUMERIC</c>: the categories POSIX defines. The GNU extras
    /// (LC_PAPER and the rest) are left to their own variables or LANG; the engine reads none of them.</summary>
    private static readonly string[] LocaleCategories = ["LC_CTYPE", "LC_COLLATE", "LC_MESSAGES", "LC_MONETARY", "LC_TIME"];

    private static InvalidOperationException NotStarted() => new("Process not started");
}
