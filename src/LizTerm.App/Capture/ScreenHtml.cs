// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;
using LizTerm.App.Rendering;
using LizTerm.Core.Screen;

namespace LizTerm.App.Capture;

/// <summary>A snapshot rendered as styled HTML, in LizTerm's own colours.
///
/// Deliberately not b3270's PrintText(html): that would add a member to IEmulatorSession for a feature needing
/// no engine, only work while connected, and emit the engine's colours rather than the ones the user is looking
/// at. See spec section 3.1.</summary>
public static class ScreenHtml
{
    /// <summary>One `pre` block, one line per row, one `span` per run of identically-styled cells.</summary>
    public static string Render(ScreenSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.Append("<pre style=\"font-family:monospace;background:#000000;padding:8px\">");

        for (var row = 0; row < snapshot.Rows; row++)
        {
            if (row > 0) sb.Append('\n');
            var cells = snapshot.Row(row);
            var column = 0;
            while (column < cells.Length)
            {
                var start = column;
                var style = cells[column];
                while (column < cells.Length && cells[column].SameStyleAs(style)) column++;
                AppendRun(sb, snapshot.GetText(row, start, column - start), style);
            }
        }

        sb.Append("</pre>");
        return sb.ToString();
    }

    private static void AppendRun(StringBuilder sb, string text, in Cell style)
    {
        var reverse = style.Rendition.HasFlag(CellRendition.Reverse);
        var foreground = Resolve(reverse ? style.Background : style.Foreground, HostColor.NeutralWhite);
        var background = Resolve(reverse ? style.Foreground : style.Background, HostColor.NeutralBlack);

        sb.Append("<span style=\"color:").Append(Hex(foreground))
          .Append(";background:").Append(Hex(background));

        // Bold, not the renderer's 35%-toward-white blend: that is a phosphor trick, and in a browser it reads
        // as washed-out text rather than as emphasis.
        if (style.Rendition.HasFlag(CellRendition.Highlight)) sb.Append(";font-weight:bold");
        if (style.Rendition.HasFlag(CellRendition.Underline)) sb.Append(";text-decoration:underline");

        // Blink is deliberately absent: a captured screen is a still.
        sb.Append("\">");
        Escape(sb, text);
        sb.Append("</span>");
    }

    private static HostColor Resolve(HostColor color, HostColor fallback) =>
        color == HostColor.Default ? fallback : color;

    private static string Hex(HostColor color)
    {
        var c = Palette.ColorOf(color);
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private static void Escape(StringBuilder sb, string text)
    {
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default: sb.Append(ch); break;
            }
        }
    }
}
