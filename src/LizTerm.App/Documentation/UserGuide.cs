// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Platform;

namespace LizTerm.App.Documentation;

/// <summary>The bundled user guide. It is an embedded application resource for the reason LICENSE and
/// THIRD-PARTY-NOTICES.txt are: nothing this pipeline packages carries loose files beside the app, and a
/// resource travels into all five package formats with no packaging work at all.
///
/// A browser cannot read an avares:// URI, so opening it means writing it out first. The HTML is generated
/// from docs/user-guide.md by UserGuideHtml in LizTerm.Core.Tests and committed; never hand-edited.</summary>
public static class UserGuide
{
    private static readonly Uri ResourceUri = new("avares://LizTerm.App/Assets/Docs/user-guide.html");

    /// <summary>Writes the guide into <paramref name="directory"/> and returns its path. The name carries the
    /// version so that an upgraded LizTerm cannot serve the previous release's page out of a temp directory the
    /// system has not yet cleaned, and is otherwise stable so that reopening Help overwrites one file rather
    /// than accumulating them.</summary>
    public static string Extract(string version, string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"lizterm-user-guide-{version}.html");

        using var resource = AssetLoader.Open(ResourceUri);
        using var file = File.Create(path);
        resource.CopyTo(file);

        return path;
    }
}
