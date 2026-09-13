// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using LizTerm.App.Status;
using SkiaSharp;

namespace LizTerm.App.Tests.Status;

/// <summary>The x3270 OIA symbols live in the bundled font as glyphs with no code point; tools/patch-3270-oia-font.py
/// gives them private-use ones and <see cref="OiaGlyphs"/> names them. Three things have to agree — the script's
/// list, the constants and the shipped file — and only these tests hold them together. Every check reads the
/// constants themselves, so a mistyped one fails here instead of drawing a box, or the wrong symbol, on the bar.</summary>
public partial class OiaGlyphsTests
{
    private static IEnumerable<string> Constants() => typeof(OiaGlyphs)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => $"{f.Name}={f.GetRawConstantValue()}");

    /// <summary>One entry per symbol of the block, in code point order: the single-character constants.</summary>
    private static List<(string Name, int CodePoint)> Symbols() => typeof(OiaGlyphs)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string) && ((string)f.GetRawConstantValue()!).Length == 1)
        .Select(f => (f.Name, CodePoint: (int)((string)f.GetRawConstantValue()!)[0]))
        .OrderBy(s => s.CodePoint)
        .ToList();

    [AvaloniaFact]
    public void Every_character_the_bar_takes_from_the_font_has_a_glyph_in_the_bundled_file()
    {
        using var typeface = OpenFont();
        var used = Constants().SelectMany(c => c[(c.IndexOf('=') + 1)..])
            .Append(StatusFormatter.PadlockGlyph.Single())
            .Distinct();
        var missing = used.Where(c => !typeface.ContainsGlyph(c)).Select(c => $"U+{(int)c:X4}").ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void The_constants_are_the_scripts_glyphs_at_the_scripts_code_points()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "patch-3270-oia-font.py"));
        var first = Convert.ToInt32(FirstCodePoint().Match(script).Groups[1].Value, 16);
        var names = GlyphName().Matches(GlyphList().Match(script).Groups[1].Value).Select(m => m.Groups[1].Value);

        Assert.Equal(
            names.Select((name, i) => $"U+{first + i:X4} {name.ToLowerInvariant()}"),
            Symbols().Select(s => $"U+{s.CodePoint:X4} {s.Name.ToLowerInvariant()}"));
    }

    /// <summary>The script maps the glyphs in the font's own order (docs/development.md), so a block whose glyphs are
    /// not in that order means the list and the constants were rearranged together, away from the font.</summary>
    [AvaloniaFact]
    public void The_block_follows_the_fonts_own_glyph_order()
    {
        using var typeface = OpenFont();
        var glyphs = Symbols().Select(s => typeface.GetGlyph(s.CodePoint)).ToList();
        Assert.Equal(glyphs.Order(), glyphs);
        Assert.Equal(glyphs.Count, glyphs.Distinct().Count());
    }

    private static SKTypeface OpenFont()
    {
        using var stream = AssetLoader.Open(new Uri("avares://LizTerm.App/Assets/Fonts/3270-Regular.otf"));
        return SKTypeface.FromStream(stream) ?? throw new InvalidOperationException("The bundled 3270 font did not load.");
    }

    /// <summary>The repository, found from this file's compile-time path the way Core.Tests' LicenseHeader.Root does,
    /// so it resolves from bin/ and on a runner alike.</summary>
    private static string RepositoryRoot([CallerFilePath] string path = "")
    {
        var dir = Path.GetDirectoryName(path);
        while (dir is not null && !File.Exists(Path.Combine(dir, "LizTerm.slnx"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException($"No LizTerm.slnx above '{path}'.");
    }

    [GeneratedRegex(@"FIRST_CODE_POINT = 0x([0-9A-Fa-f]+)")]
    private static partial Regex FirstCodePoint();

    [GeneratedRegex(@"GLYPHS = \[(.*?)\]", RegexOptions.Singleline)]
    private static partial Regex GlyphList();

    [GeneratedRegex("\"([^\"]+)\"")]
    private static partial Regex GlyphName();
}
