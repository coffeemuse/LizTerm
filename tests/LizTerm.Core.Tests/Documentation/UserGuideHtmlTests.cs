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
}
