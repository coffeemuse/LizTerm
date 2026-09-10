// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Capture;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.Capture;

public class ScreenHtmlTests
{
    private static ScreenSnapshot One(string text, HostColor? foreground = null,
        HostColor? background = null, CellRendition? rendition = null)
    {
        var buffer = new ScreenBuffer(1, 20);
        buffer.SetText(0, 0, text, foreground, background, rendition);
        return buffer.Snapshot();
    }

    [Fact]
    public void It_wraps_the_screen_in_a_pre_block()
    {
        var html = ScreenHtml.Render(One("READY"));

        Assert.StartsWith("<pre", html);
        Assert.EndsWith("</pre>", html);
    }

    [Fact]
    public void A_run_carries_its_foreground_as_a_hex_colour()
    {
        var html = ScreenHtml.Render(One("READY", HostColor.Green));

        Assert.Contains("color:#50FF50", html);
        Assert.Contains("READY", html);
    }

    [Fact]
    public void Highlight_becomes_bold_rather_than_a_lightened_colour()
    {
        var html = ScreenHtml.Render(One("HOT", HostColor.Green, null, CellRendition.Highlight));

        Assert.Contains("font-weight:bold", html);
        Assert.Contains("color:#50FF50", html);
    }

    [Fact]
    public void Underline_becomes_a_text_decoration()
    {
        Assert.Contains("text-decoration:underline",
            ScreenHtml.Render(One("X", null, null, CellRendition.Underline)));
    }

    /// <summary>Reverse video swaps the two colours, exactly as the renderer does.</summary>
    [Fact]
    public void Reverse_swaps_the_colours()
    {
        var html = ScreenHtml.Render(One("X", HostColor.Green, HostColor.Red, CellRendition.Reverse));

        Assert.Contains("color:#FF5050", html);
        Assert.Contains("background:#50FF50", html);
    }

    /// <summary>A still image has no blink. Asserted so its absence reads as a decision rather than an
    /// oversight.</summary>
    [Fact]
    public void Blink_is_not_emitted()
    {
        var html = ScreenHtml.Render(One("X", null, null, CellRendition.Blink));

        Assert.DoesNotContain("blink", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Markup_characters_on_the_screen_are_escaped()
    {
        var html = ScreenHtml.Render(One("<b>&</b>"));

        Assert.Contains("&lt;b&gt;&amp;&lt;/b&gt;", html);
        Assert.DoesNotContain("<b>", html);
    }

    /// <summary>The same segmentation rule the renderer uses: equal foreground, background and rendition. Three
    /// differently-coloured stretches are three spans, not one and not nine.</summary>
    [Fact]
    public void Adjacent_differing_styles_become_separate_spans()
    {
        var buffer = new ScreenBuffer(1, 20);
        buffer.SetText(0, 0, "aaa", HostColor.Green, null, null);
        buffer.SetText(0, 3, "bbb", HostColor.Red, null, null);
        buffer.SetText(0, 6, "ccc", HostColor.Green, null, null);

        var html = ScreenHtml.Render(buffer.Snapshot());

        // Three coloured runs plus the trailing blank run to the end of the row.
        Assert.Equal(4, html.Split("<span").Length - 1);
    }

    [Fact]
    public void Every_row_becomes_its_own_line()
    {
        var buffer = new ScreenBuffer(3, 5);
        buffer.SetText(0, 0, "one", null, null, null);
        buffer.SetText(1, 0, "two", null, null, null);
        buffer.SetText(2, 0, "six", null, null, null);

        var html = ScreenHtml.Render(buffer.Snapshot());

        Assert.Equal(2, html.Split('\n').Length - 1);
    }
}
