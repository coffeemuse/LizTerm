// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.RegularExpressions;
using LizTerm.Core.Tests.Repository;

namespace LizTerm.Core.Tests.Documentation;

/// <summary>Holds the committed HTML to its Markdown source, and the Markdown source to what the converter can
/// actually render. Lives in Core.Tests for the reason RepositoryHeadersTests does: this project builds on
/// every run, so a guide edited without regenerating fails on the developer's machine rather than in CI.</summary>
public class UserGuideAssetTests
{
    private static string Root => LicenseHeader.Root();
    private static string MarkdownPath => Path.Combine(Root, "docs", "user-guide.md");
    private static string HtmlPath =>
        Path.Combine(Root, "src", "LizTerm.App", "Assets", "Docs", "user-guide.html");

    private static string Markdown() => File.ReadAllText(MarkdownPath).ReplaceLineEndings("\n");

    /// <summary>The version the release pipeline will agree with: release.yml's version job reads this same
    /// element and fails the run unless the tag and LizTerm.parcel match it.</summary>
    private static string Version()
    {
        var match = Regex.Match(File.ReadAllText(Path.Combine(Root, "Directory.Build.props")),
            @"<Version>([^<]+)</Version>");
        Assert.True(match.Success, "no <Version> in Directory.Build.props");
        return match.Groups[1].Value;
    }

    [Fact]
    public void The_committed_html_is_what_the_converter_produces()
    {
        var expected = UserGuideHtml.Page(Markdown(), Version());

        if (Environment.GetEnvironmentVariable("LIZTERM_UPDATE_DOCS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HtmlPath)!);
            File.WriteAllText(HtmlPath, expected);
            return;
        }

        Assert.True(File.Exists(HtmlPath),
            $"{HtmlPath} is missing. Regenerate with LIZTERM_UPDATE_DOCS=1 (see docs/development.md).");
        // Normalized on both sides: this repository has no .gitattributes and the suite runs on windows-latest.
        Assert.Equal(expected, File.ReadAllText(HtmlPath).ReplaceLineEndings("\n"));
    }

    /// <summary>The converter handles exactly what the guide uses today. Its failure mode is silence — an
    /// unsupported construct is dropped or emitted as literal text — so the source is held to the supported set
    /// rather than the output inspected afterwards for damage.</summary>
    [Fact]
    public void The_guide_uses_no_construct_the_converter_cannot_render()
    {
        var unsupported = new (string What, Regex Pattern)[]
        {
            ("an ordered list", new Regex(@"^\d+\. ", RegexOptions.Multiline)),
            ("a blockquote", new Regex("^> ", RegexOptions.Multiline)),
            // [ \t], not \s: \s matches \n, so under Multiline "^\s+" can consume a blank line's own
            // newline and keep going, matching a following top-level bullet that isn't nested at all.
            ("a nested list", new Regex(@"^[ \t]+[-*] ", RegexOptions.Multiline)),
            ("a horizontal rule", new Regex("^---+$", RegexOptions.Multiline)),
            ("an asterisk bullet", new Regex(@"^\* ", RegexOptions.Multiline)),
            ("an image", new Regex(@"!\[")),
            ("raw HTML", new Regex("^<", RegexOptions.Multiline)),
            ("an escaped pipe in a table", new Regex(@"\\\|")),
            ("a reference link", new Regex(@"^\[[^\]]+\]:", RegexOptions.Multiline)),
            // [^*\n] and [^\]\n], not [^*] and [^\]]: without excluding \n these spans cross paragraph/list/table
            // boundaries and pick up unrelated ** ... [ ] pairs. Without a trailing \*\* this also can't tell a
            // closing ** from an opening one, so "**bold** text [link](url)" false-matches on the closing delimiter.
            ("a link inside bold", new Regex(@"\*\*[^*\n]*\[[^\]\n]*\]\([^)\n]*\)[^*\n]*\*\*")),
        };

        var markdown = Markdown();
        foreach (var (what, pattern) in unsupported)
        {
            Assert.False(pattern.IsMatch(markdown),
                $"docs/user-guide.md now uses {what}, which UserGuideHtml does not render. Extend the converter and its tests, then regenerate.");
        }
    }

    [Fact]
    public void Every_internal_link_resolves_to_a_heading_the_page_emits()
    {
        var markdown = Markdown();
        var ids = Regex.Matches(markdown, @"^#{1,6} (.+)$", RegexOptions.Multiline)
            .Select(m => UserGuideHtml.Slug(m.Groups[1].Value.Trim()))
            .ToHashSet();

        foreach (Match link in Regex.Matches(markdown, @"\]\(#([^)]+)\)"))
        {
            Assert.Contains(link.Groups[1].Value, ids);
        }
    }

    [Fact]
    public void The_banner_names_the_version_the_release_will_carry() =>
        Assert.Contains($"Offline copy, shipped with LizTerm {Version()}.",
            File.ReadAllText(HtmlPath).ReplaceLineEndings("\n"));

    /// <summary>Every link in the guide must have become an anchor. A link the converter mishandled survives
    /// into the page as a literal "](", which is unambiguous in a way that pattern-matching the Markdown source
    /// for hazards is not — the source-side guards above are necessarily approximations, and this is not.
    /// The source has 18 links and no "](" inside a code span, so the rendered page should contain none.</summary>
    [Fact]
    public void No_link_survives_unrendered_in_the_page() =>
        Assert.DoesNotContain("](", File.ReadAllText(HtmlPath).ReplaceLineEndings("\n"));
}
