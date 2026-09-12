// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;
using System.Text.RegularExpressions;

namespace LizTerm.Core.Tests.Documentation;

/// <summary>Converts docs/user-guide.md to the single self-contained page the app embeds. Test-only code: no
/// src/ project references it, so the shipped binary carries the HTML and none of this.
///
/// It is not a Markdown implementation. It handles exactly the constructs the guide uses, and
/// UserGuideAssetTests fails if the guide grows one it does not — which is what makes a converter this small
/// safe to own, because the failure mode of a partial converter is silence.</summary>
public static partial class UserGuideHtml
{
    /// <summary>The document body. <see cref="Page"/>, added in a later task, wraps it.</summary>
    public static string Convert(string markdown, string version)
    {
        // Line endings are normalized on the way in and emitted as \n throughout. The committed HTML is
        // compared against this output on runners whose checkout may have rewritten either file, and this
        // repository has no .gitattributes.
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var body = new StringBuilder();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                var code = new StringBuilder();
                for (i++; i < lines.Length && !lines[i].StartsWith("```", StringComparison.Ordinal); i++)
                {
                    code.Append(Escape(lines[i])).Append('\n');
                }
                body.Append("<pre><code>").Append(code).Append("</code></pre>\n");
                continue;
            }

            if (line.StartsWith('#'))
            {
                var level = line.Length - line.TrimStart('#').Length;
                var text = line[level..].Trim();
                body.Append($"<h{level} id=\"{Slug(text)}\">").Append(Inline(text, version)).Append($"</h{level}>\n");
                continue;
            }

            if (line.StartsWith('|'))
            {
                var rows = new List<string[]>();
                for (; i < lines.Length && lines[i].StartsWith('|'); i++) rows.Add(Cells(lines[i]));
                i--;

                body.Append("<table>\n<thead>\n<tr>");
                foreach (var cell in rows[0]) body.Append("<th>").Append(Inline(cell, version)).Append("</th>");
                body.Append("</tr>\n</thead>\n<tbody>\n");
                // rows[1] is the |---|---| separator, which carries no content.
                foreach (var row in rows.Skip(2))
                {
                    body.Append("<tr>");
                    foreach (var cell in row) body.Append("<td>").Append(Inline(cell, version)).Append("</td>");
                    body.Append("</tr>\n");
                }
                body.Append("</tbody>\n</table>\n");
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                body.Append("<ul>\n");
                for (; i < lines.Length && lines[i].StartsWith("- ", StringComparison.Ordinal); i++)
                {
                    body.Append("<li>").Append(Inline(lines[i][2..], version)).Append("</li>\n");
                }
                i--;
                body.Append("</ul>\n");
                continue;
            }

            var paragraph = new List<string>();
            for (; i < lines.Length && lines[i].Length > 0; i++) paragraph.Add(lines[i]);
            i--;
            body.Append("<p>").Append(Inline(string.Join('\n', paragraph), version)).Append("</p>\n");
        }

        return body.ToString();
    }

    /// <summary>The id GitHub would have given the heading, so that the guide's own table of contents resolves
    /// inside this page: lowercased, punctuation dropped, spaces hyphenated.</summary>
    public static string Slug(string heading)
    {
        var slug = new StringBuilder();
        foreach (var c in heading.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) slug.Append(c);
            else if (c is ' ' or '-') slug.Append('-');
        }
        return slug.ToString();
    }

    /// <summary>The cells of one row. The guide escapes no pipes — UserGuideAssetTests holds it to that — so a
    /// plain split is correct here, and a cell that grew a literal pipe would be caught there rather than
    /// silently split into two.</summary>
    private static string[] Cells(string row) =>
        row.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private const string Repo = "https://github.com/coffeemuse/LizTerm";

    /// <summary>One alternation over every inline construct, matched left to right. A single pass rather than
    /// four sequential replacements: sequential passes would have to hide code spans behind a placeholder to
    /// stop emphasis being applied inside them, and a placeholder is a token that can be collided with. Here
    /// a code span simply wins its own match, and nothing else looks inside it.</summary>
    [GeneratedRegex(@"`([^`]*)`|\[([^\]]+)\]\(([^)]+)\)|\*\*([^*]+)\*\*|(?<!\*)\*([^*]+)\*(?!\*)")]
    private static partial Regex Spans();

    private static string Inline(string text, string version)
    {
        var html = new StringBuilder();
        var at = 0;

        foreach (Match m in Spans().Matches(text))
        {
            // Text between constructs is escaped; each construct escapes its own content below.
            html.Append(Escape(text[at..m.Index]));

            if (m.Groups[1].Success) html.Append("<code>").Append(Escape(m.Groups[1].Value)).Append("</code>");
            else if (m.Groups[2].Success)
            {
                html.Append($"<a href=\"{Href(m.Groups[3].Value, version)}\">")
                    .Append(Escape(m.Groups[2].Value)).Append("</a>");
            }
            else if (m.Groups[4].Success) html.Append("<strong>").Append(Escape(m.Groups[4].Value)).Append("</strong>");
            else html.Append("<em>").Append(Escape(m.Groups[5].Value)).Append("</em>");

            at = m.Index + m.Length;
        }

        return html.Append(Escape(text[at..])).ToString();
    }

    /// <summary>Spec §5.1: a link that is part of the document's own content points at the tag, so it describes
    /// the release the reader is running. The banner's link — the one asking "has this changed?" — is the
    /// deliberate exception and points at main; it is written in Page, not here.</summary>
    private static string Href(string target, string version) => target switch
    {
        "../README.md" => $"{Repo}/blob/v{version}/README.md",
        "../README.md#first-run" => $"{Repo}/blob/v{version}/README.md#first-run",
        _ => target,
    };
}
