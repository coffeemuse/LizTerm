// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Session;

/// <summary>A code page b3270 knows, with a human label. The engine name leads the display because that is what
/// goes on the command line and what appears in a wire log; the label is curated from the engine's own aliases
/// rather than shown raw, because cp1047 has no alias at all and several others are terse (uk, us-intl, oldibm).
///
/// A mistyped code page does not fail: b3270 warns on stderr, starts anyway on a fallback, and echoes the bad
/// name back in its settings — and LizTerm surfaces its stderr tail only on a startup timeout or process death.
/// So a typo gave a session that connected normally with quietly wrong characters. That is what the drop-down
/// is for, over and above friendliness.
///
/// Order is the engine's own — ascending numeric — with bracket moved to the front: it is what this project's
/// TK5 sample profile uses and is named nothing like a code page. Alphabetical order would sort cp1026 ahead of
/// cp273 and scatter the families, since these are strings.
/// Measured against b3270 4.5ga6 on 2026-09-09: 41 entries.</summary>
public sealed record CodePage(string Name, string Label)
{
    public static IReadOnlyList<CodePage> All { get; } =
    [
        new("bracket", "US English (3270 brackets) — TK4-/TK5"),
        new("cp037", "US / Canada"),
        new("cp273", "German"),
        new("cp275", "Brazilian"),
        new("cp277", "Norwegian"),
        new("cp278", "Finnish / Swedish"),
        new("cp280", "Italian"),
        new("cp284", "Spanish"),
        new("cp285", "United Kingdom"),
        new("cp297", "French"),
        new("cp424", "Hebrew"),
        new("cp500", "Belgian"),
        new("cp803", "Hebrew (old)"),
        new("cp870", "Polish / Slovenian"),
        new("cp871", "Icelandic"),
        new("cp875", "Greek"),
        new("cp880", "Russian"),
        new("cp930", "Japanese (katakana)"),
        new("cp933", "Korean"),
        new("cp935", "Simplified Chinese"),
        new("cp937", "Traditional Chinese"),
        new("cp939", "Japanese (Latin)"),
        new("cp1026", "Turkish"),
        new("cp1047", "Latin-1 / Open Systems"),
        new("cp1123", "Ukrainian"),
        new("cp1140", "US / Canada (Euro)"),
        new("cp1141", "German (Euro)"),
        new("cp1142", "Norwegian (Euro)"),
        new("cp1143", "Finnish / Swedish (Euro)"),
        new("cp1144", "Italian (Euro)"),
        new("cp1145", "Spanish (Euro)"),
        new("cp1146", "United Kingdom (Euro)"),
        new("cp1147", "French (Euro)"),
        new("cp1148", "Belgian (Euro)"),
        new("cp1149", "Icelandic (Euro)"),
        new("cp1158", "Ukrainian (Euro)"),
        new("cp1160", "Thai"),
        new("cp1364", "Korean (Euro)"),
        new("cp1388", "Chinese (GB18030)"),
        new("cp1390", "Japanese (katakana, extended)"),
        new("cp1399", "Japanese (Latin, extended)"),
    ];

    public static CodePage? Find(string name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>What the editor's drop-down shows.</summary>
    public override string ToString() => $"{Name} — {Label}";
}
