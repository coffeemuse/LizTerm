// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Platform;

namespace LizTerm.App;

/// <summary>LizTerm's own copyright and license, in the two forms the UI shows them: a one-line credit for the
/// splash and About, and the full text ahead of the bundled components' notices.</summary>
/// <remarks>LICENSE is an embedded application resource because About is the only place it reaches a user: no
/// archive or installer this project produces carries a LICENSE file beside the app, so this is what satisfies
/// BSD-3-Clause clause 2 for a binary distribution.</remarks>
public static class AppLicense
{
    public const string SpdxId = "BSD-3-Clause";

    /// <summary>Worded exactly as the LICENSE file's own first line.</summary>
    public const string Copyright = "Copyright 2026 by CoffeeMuse";

    /// <summary>The one-line credit. ASCII on purpose, hyphen and all: the splash renders this in the 3270 font,
    /// whose coverage is not general -- the status bar's padlock already needs a private-use codepoint.</summary>
    public const string Notice = Copyright + " - " + SpdxId;

    private static readonly Uri LicenseUri = new("avares://LizTerm.App/Assets/LICENSE.txt");
    private static readonly Uri PhotoLicenseUri = new("avares://LizTerm.App/Assets/Liz/LICENSE-liz.txt");
    private static readonly Uri NoticesUri = new("avares://LizTerm.App/Assets/THIRD-PARTY-NOTICES.txt");

    private static string? _all;

    /// <summary>Every license the shipped app is covered by: LizTerm's own first, then the photo of Liz's -- also
    /// LizTerm's, but CC BY-NC-ND rather than BSD -- then the bundled components'. Read on first use rather than
    /// in a static initializer, so that touching <see cref="Notice"/> -- a compile-time constant the splash reads
    /// while the app is still starting -- can never pull the asset loader in with it. Read once thereafter:
    /// About can be reopened any number of times and the bytes never change. UI thread only.</summary>
    public static string All => _all ??= Read();

    private static string Read()
    {
        var text = $"LizTerm, {SpdxId}\n\n{ReadAsset(LicenseUri)}\n\n{ReadAsset(PhotoLicenseUri)}\n\n{ReadAsset(NoticesUri)}\n";
        // The two files are stored with \n; a TextBox on Windows wants the platform's own.
        return text.ReplaceLineEndings();
    }

    private static string ReadAsset(Uri uri)
    {
        using var stream = AssetLoader.Open(uri);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
