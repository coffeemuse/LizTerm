// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Runtime.CompilerServices;
using System.Text;
using LizTerm.Backend.B3270.Process;

namespace LizTerm.Backend.B3270.Tests.Process;

public class EnginePatchesTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "lizterm-patches-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Finds_the_marker_among_other_bytes()
    {
        File.WriteAllBytes(_path, [0x00, 0xff, .. Encoding.ASCII.GetBytes("OtherOptions"), 0x00, .. Encoding.ASCII.GetBytes(EnginePatches.CommandPrefixMarker), 0x00, 0x7f]);
        Assert.True(EnginePatches.Carries(_path, EnginePatches.CommandPrefixMarker));
    }

    [Fact]
    public void Does_not_find_a_marker_that_is_absent_or_only_partly_there()
    {
        File.WriteAllBytes(_path, [0x00, .. Encoding.ASCII.GetBytes("OtherOptions\0CommandPrefiX\0CommandPrefi")]);
        Assert.False(EnginePatches.Carries(_path, EnginePatches.CommandPrefixMarker));
    }

    /// <summary>The build gate and the backend must look for the same string, or a gated engine could still be refused.</summary>
    [Fact]
    public void The_marker_is_the_one_the_build_gate_checks()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "native", "build", "shared-verify-patches.sh"));
        Assert.Contains($"MARKERS=({EnginePatches.CommandPrefixMarker})", script);
    }

    /// <summary>This file sits at tests/LizTerm.Backend.B3270.Tests/Process/, three levels below the root.</summary>
    private static string RepositoryRoot([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", ".."));
}
