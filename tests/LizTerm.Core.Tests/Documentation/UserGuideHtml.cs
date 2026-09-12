// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;

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

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // Task 5 replaces this with real inline handling.
    private static string Inline(string text, string version) => Escape(text);
}
