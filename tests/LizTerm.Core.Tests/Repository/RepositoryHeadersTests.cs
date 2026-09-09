// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Tests.Repository;

/// <summary>The repository's own hygiene, not Core's behaviour -- but it lives here because Core.Tests is the
/// project every run builds, so a new file without a header fails locally before it is ever pushed.</summary>
public class RepositoryHeadersTests
{
    /// <summary>The directories that hold hand-written source. Build and configuration files (csproj, the props
    /// files, LizTerm.slnx, LizTerm.parcel, the workflow YAML) are deliberately not in scope.</summary>
    private static readonly string[] Roots = ["src", "tests", "native/build", "tools"];

    private static readonly string[] Extensions = [".cs", ".axaml", ".sh"];

    [Fact]
    public void Every_source_file_carries_the_license_header()
    {
        var root = LicenseHeader.Root();
        var missing = new List<string>();

        foreach (var relative in Roots)
        {
            var directory = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(Directory.Exists(directory), $"'{relative}' is not in the repository -- fix Roots.");

            var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.Ordinal))
                .Where(f => !IsBuildOutput(f, root))
                .ToList();

            // A root that matches nothing would let this whole test pass without reading a single file.
            Assert.True(files.Count > 0, $"'{relative}' contributed no source files -- fix Roots or Extensions.");

            missing.AddRange(files
                .Where(f => !LicenseHeader.IsPresent(File.ReadAllText(f)))
                .Select(f => Path.GetRelativePath(root, f)));
        }

        Assert.True(missing.Count == 0,
            $"{missing.Count} file(s) are missing the license header:{Environment.NewLine}"
            + string.Join(Environment.NewLine, missing.Order(StringComparer.Ordinal))
            + $"{Environment.NewLine}{Environment.NewLine}Add these three lines at the top, in the file's own comment syntax:"
            + $"{Environment.NewLine}  {LicenseHeader.Provenance}"
            + $"{Environment.NewLine}  {LicenseHeader.Copyright}"
            + $"{Environment.NewLine}  {LicenseHeader.Spdx}");
    }

    private static bool IsBuildOutput(string file, string root) =>
        Path.GetRelativePath(root, file)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "obj" or "bin");
}
