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
    public async Task Our_model_table_is_the_engines()
    {
        var initialize = await ReadInitializeAsync();
        var models = Block(initialize, "models").EnumerateArray()
            .Select(m => new TerminalModel(m.GetProperty("model").GetInt32(), m.GetProperty("rows").GetInt32(), m.GetProperty("columns").GetInt32()))
            .ToList();

        Assert.Equal(TerminalModel.All.OrderBy(m => m.Number), models.OrderBy(m => m.Number));
    }

    [Fact(Timeout = 60_000)]
    public async Task Our_code_page_names_are_the_engines()
    {
        var initialize = await ReadInitializeAsync();
        var names = Block(initialize, "code-pages").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString()!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        // Names only. The labels are ours -- curated from the engine's aliases, which are terse or absent -- so
        // they are deliberately not asserted here.
        Assert.Equal(CodePage.All.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal), names);
    }

    /// <summary>b3270's initialize block is its first stdout line. Read directly rather than through
    /// B3270Session, which parses out hello and tls-hello and keeps neither of these.
    ///
    /// The read is bounded so a hung or corrupt engine can never orphan the child process: xunit's
    /// <c>Timeout</c> on a test method does not abort a blocked read, so the wait has to be bounded here and
    /// the kill tied to it with a <c>finally</c> that runs whether the read completed, failed, or timed out.
    /// Standard error is left unredirected rather than drained: nothing here reads it, and a redirected but
    /// undrained pipe is the exact same hang by another door if the engine ever fills its buffer before
    /// writing the first stdout line.</summary>
    private static async Task<JsonElement> ReadInitializeAsync()
    {
        var location = BundledEngine.Require();
        using var process = Process.Start(new ProcessStartInfo(location.Path, "-json")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        })!;
        try
        {
            string? line;
            try
            {
                line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                Assert.Fail("the engine produced no initialize line within 10s");
                return default;
            }

            Assert.False(string.IsNullOrWhiteSpace(line), "the engine produced no initialize line");
            return JsonDocument.Parse(line!).RootElement.Clone();
        }
        finally
        {
            try { await process.StandardInput.WriteLineAsync("""{"run":{"r-tag":"q","actions":[{"action":"Quit"}]}}"""); } catch { /* already gone */ }
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
            }
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
