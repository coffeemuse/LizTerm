// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Tests.Documentation;

public class UserGuideHtmlTests
{
    private static string Body(string markdown) => UserGuideHtml.Convert(markdown, "9.9.9");

    [Fact]
    public void Headings_carry_the_id_their_anchors_use()
    {
        Assert.Contains("<h2 id=\"tls-and-certificates\">TLS and certificates</h2>", Body("## TLS and certificates"));
        Assert.Contains("<h3 id=\"profile-settings\">Profile settings</h3>", Body("### Profile settings"));
    }

    [Theory]
    [InlineData("File transfer (IND$FILE)", "file-transfer-indfile")]
    [InlineData("Mouse, selection and clipboard", "mouse-selection-and-clipboard")]
    [InlineData("Where LizTerm keeps its files", "where-lizterm-keeps-its-files")]
    public void Slugs_match_the_ids_GitHub_would_have_produced(string heading, string expected) =>
        Assert.Equal(expected, UserGuideHtml.Slug(heading));

    [Fact]
    public void Consecutive_lines_become_one_paragraph()
    {
        var html = Body("one line\nand its continuation\n\na second paragraph");

        Assert.Contains("<p>one line\nand its continuation</p>", html);
        Assert.Contains("<p>a second paragraph</p>", html);
    }

    [Fact]
    public void Consecutive_dashes_become_one_list()
    {
        var html = Body("- first\n- second\n");

        Assert.Contains("<ul>\n<li>first</li>\n<li>second</li>\n</ul>", html);
    }

    [Fact]
    public void A_fenced_block_keeps_its_lines_and_drops_its_language()
    {
        var html = Body("```text\nREADY\nLOGON\n```");

        Assert.Contains("<pre><code>READY\nLOGON\n</code></pre>", html);
        Assert.DoesNotContain("text", html);
    }

    [Fact]
    public void Bold_and_italic_and_code_become_their_elements()
    {
        Assert.Contains("<strong>Connect</strong>", Body("Choose **Connect** now."));
        Assert.Contains("<em>pin</em>", Body("to *pin* the certificate"));
        Assert.Contains("<code>settings.json</code>", Body("`settings.json` holds your preferences"));
    }

    /// <summary>The guide's line 252 carries `wire-&lt;profile&gt;-&lt;date&gt;-&lt;time&gt;.log`. A converter
    /// that emits a code span verbatim hands the browser an unknown element, which renders as nothing, and the
    /// user is shown "wire-.log" — wrong, plausible, and shipped offline where nobody can correct it.</summary>
    [Fact]
    public void A_code_span_escapes_its_contents()
    {
        var html = Body("`wire-<profile>-<date>.log` names the file");

        Assert.Contains("<code>wire-&lt;profile&gt;-&lt;date&gt;.log</code>", html);
        Assert.DoesNotContain("<profile>", html);
    }

    [Fact]
    public void An_ampersand_outside_a_code_span_is_escaped_too() =>
        Assert.Contains("Edit &amp; View", Body("Edit & View"));

    [Fact]
    public void Emphasis_inside_a_code_span_is_left_alone() =>
        Assert.Contains("<code>a*b*c</code>", Body("`a*b*c`"));

    [Fact]
    public void Links_keep_their_target()
    {
        Assert.Contains("<a href=\"#keyboard\">Keyboard</a>", Body("[Keyboard](#keyboard)"));
        Assert.Contains("<a href=\"https://example.invalid/x\">there</a>", Body("[there](https://example.invalid/x)"));
    }

    [Fact]
    public void A_relative_readme_link_becomes_an_absolute_one_at_the_release_tag()
    {
        var html = Body("see the [README](../README.md#first-run)");

        Assert.Contains("href=\"https://github.com/coffeemuse/LizTerm/blob/v9.9.9/README.md#first-run\"", html);
        Assert.DoesNotContain("../README.md", html);
    }
}
