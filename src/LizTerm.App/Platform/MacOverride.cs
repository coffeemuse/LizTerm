// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Logging;

namespace LizTerm.App.Platform;

/// <summary>What LizTerm's macOS overrides share (Menus/MacMenuKeyEquivalents and Keyboard/MacEscapeChords): one
/// attempt per process under a lock, Installed for whatever depends on the override being in place, and a failure
/// that names its step and its consequence in the trace log (where Program's LogToTrace sends it) and degrades to
/// the old behaviour. Install runs from App before any window exists, so nothing here may take down launch. Each
/// override keeps only its own steps and its rule.</summary>
internal sealed class MacOverride(string name, string consequence)
{
    private readonly object _gate = new();

    public bool Installed { get; private set; }

    /// <summary>Runs add once: it answers null when the method is in place, or the step that failed, and an
    /// exception is a failure like any other. False off macOS.</summary>
    public bool Install(bool isMacOS, Func<string?> add)
    {
        if (!isMacOS) return false;
        lock (_gate)
        {
            if (Installed) return true;
            string? failure;
            try
            {
                failure = add();
            }
            catch (Exception ex)
            {
                failure = ex.ToString();
            }
            if (failure is null)
            {
                Installed = true;
                return true;
            }
            Logger.TryGet(LogEventLevel.Warning, LogArea.Platform)
                ?.Log(null, "{Name} not installed ({Step}); {Consequence}", name, failure, consequence);
            return false;
        }
    }
}
