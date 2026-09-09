// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Diagnostics;
using System.Text.Json;
using LizTerm.Core.Session;

namespace LizTerm.Integration.Tests;

/// <summary>The profile editor's model and code page lists are static tables, because the editor opens from the
/// picker with no engine running. This is what stops them drifting: an x3270 bump that adds, removes or renames
/// an entry fails here instead of silently offering a list the engine does not share.</summary>
public class EngineCatalogueTests
{
    [Fact(Timeout = 60_000)]
    public void Our_model_table_is_the_engines()
    {
        var initialize = ReadInitialize();
        var models = Block(initialize, "models").EnumerateArray()
            .Select(m => new TerminalModel(m.GetProperty("model").GetInt32(), m.GetProperty("rows").GetInt32(), m.GetProperty("columns").GetInt32()))
            .ToList();

        Assert.Equal(models.OrderBy(m => m.Number), TerminalModel.All.OrderBy(m => m.Number));
    }

    [Fact(Timeout = 60_000)]
    public void Our_code_page_names_are_the_engines()
    {
        var initialize = ReadInitialize();
        var names = Block(initialize, "code-pages").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString()!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        // Names only. The labels are ours -- curated from the engine's aliases, which are terse or absent -- so
        // they are deliberately not asserted here.
        Assert.Equal(names, CodePage.All.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>b3270's initialize block is its first stdout line. Read directly rather than through
    /// B3270Session, which parses out hello and tls-hello and keeps neither of these.</summary>
    private static JsonElement ReadInitialize()
    {
        var location = BundledEngine.Require();
        using var process = Process.Start(new ProcessStartInfo(location.Path, "-json")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        try
        {
            var line = process.StandardOutput.ReadLine();
            Assert.False(string.IsNullOrWhiteSpace(line), "the engine produced no initialize line");
            return JsonDocument.Parse(line!).RootElement.Clone();
        }
        finally
        {
            try { process.StandardInput.WriteLine("""{"run":{"r-tag":"q","actions":[{"action":"Quit"}]}}"""); } catch { /* already gone */ }
            if (!process.WaitForExit(5_000)) process.Kill(entireProcessTree: true);
        }
    }

    /// <summary>The initialize payload is an array of single-key objects, so a named block is found rather than
    /// indexed.</summary>
    private static JsonElement Block(JsonElement root, string name)
    {
        var payload = root.TryGetProperty("initialize", out var wrapped) ? wrapped : root;
        if (payload.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in payload.EnumerateArray())
                if (entry.TryGetProperty(name, out var found)) return found;
        }
        else if (payload.TryGetProperty(name, out var direct)) return direct;

        Assert.Fail($"the engine's initialize block has no \"{name}\"");
        return default;
    }
}
