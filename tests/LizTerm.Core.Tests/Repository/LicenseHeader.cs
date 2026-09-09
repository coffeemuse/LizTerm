// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Runtime.CompilerServices;

namespace LizTerm.Core.Tests.Repository;

/// <summary>The repository's per-file license header, and where the repository is. Pure and testable on its own,
/// so the guard that walks the tree can be proven to reject as well as to accept -- a guard that cannot fail is
/// not a guard.</summary>
internal static class LicenseHeader
{
    public const string Provenance = "This file is part of LizTerm.";
    public const string Copyright = "Copyright 2026 by CoffeeMuse";
    public const string Spdx = "SPDX-License-Identifier: BSD-3-Clause";

    /// <summary>How far into a file the header may start. A shell script spends line 1 on its shebang and an
    /// .axaml file spends it on the comment's opening delimiter, so the header does not always begin at line 1;
    /// past this it is no longer a header.</summary>
    private const int Window = 8;

    /// <summary>True when the three lines appear in order within the first <see cref="Window"/> lines. Each is
    /// matched as the tail of its line, which is what lets one rule cover "// ", "# " and the plain indented text
    /// inside an XML comment block without knowing which kind of file it is looking at.</summary>
    public static bool IsPresent(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var at = 0;
        foreach (var expected in new[] { Provenance, Copyright, Spdx })
        {
            while (at < lines.Length && at < Window && !lines[at].TrimEnd().EndsWith(expected, StringComparison.Ordinal)) at++;
            if (at >= lines.Length || at >= Window) return false;
            at++;
        }
        return true;
    }

    /// <summary>The repository root, found by walking up from this file's own compile-time path to the directory
    /// holding LizTerm.slnx. The path is baked in at build time, so it resolves the same whether the test runs
    /// from bin/ locally or on a runner, and it does not depend on the working directory.</summary>
    public static string Root([CallerFilePath] string callerPath = "")
    {
        var dir = Path.GetDirectoryName(callerPath);
        while (dir is not null && !File.Exists(Path.Combine(dir, "LizTerm.slnx"))) dir = Path.GetDirectoryName(dir);
        // Failing is the point: a root this cannot find means the walk below would scan nothing and pass vacuously.
        return dir ?? throw new InvalidOperationException($"No LizTerm.slnx above '{callerPath}'.");
    }
}
