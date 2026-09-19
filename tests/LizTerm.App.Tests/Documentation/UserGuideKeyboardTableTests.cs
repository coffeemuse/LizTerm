// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia.Input;
using LizTerm.App.Keyboard;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Documentation;

/// <summary>Holds the user guide's Keyboard table to DefaultKeymap (editable keymap spec §6.1, #48): every default
/// chord appears in the row for its key, and no row claims a chord the default lacks. The guide is hand-ordered
/// prose in a table, so the table is read with a small fixed vocabulary; a row that says something this reader does
/// not know fails with the row's own words, and the fix is to extend the vocabulary or reword the row. The live
/// table is the Keyboard tab; this is what keeps the guide's copy of the defaults honest.</summary>
public partial class UserGuideKeyboardTableTests
{
    private static readonly Dictionary<string, Key> KeyWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = Key.Enter, ["Escape"] = Key.Escape, ["Pause"] = Key.Pause, ["Page Up"] = Key.PageUp,
        ["Page Down"] = Key.PageDown, ["Home"] = Key.Home, ["End"] = Key.End, ["Insert"] = Key.Insert,
        ["Delete"] = Key.Delete, ["Tab"] = Key.Tab, ["R"] = Key.R, ["I"] = Key.I,
        ["1"] = Key.D1, ["2"] = Key.D2, ["3"] = Key.D3, ["6"] = Key.D6, ["["] = Key.OemOpenBrackets,
    };

    private static readonly KeyChord Backspace = new(Key.Back);

    [GeneratedRegex(@"^F(\d{1,2})$")]
    private static partial Regex FunctionKey();

    [Fact]
    public void The_guides_keyboard_table_is_the_default_keymap()
    {
        var keyClaims = new Dictionary<KeyChord, TerminalKey>();
        var textClaims = new Dictionary<KeyChord, string>();
        var sawBackspace = false;

        foreach (var (keys, action) in Rows())
        {
            if (keys == "Backspace")
            {
                sawBackspace = true;
                continue;
            }
            var groups = Groups(keys);
            if (action.StartsWith("Types `", StringComparison.Ordinal))
            {
                var text = action.Split('`')[1];
                foreach (var chord in groups.SelectMany(g => g))
                    Assert.True(textClaims.TryAdd(chord, text), $"The guide lists {ChordSyntax.Format(chord)} twice.");
                continue;
            }
            var actions = Actions(action, keys);
            foreach (var group in groups)
            {
                Assert.True(actions.Count == 1 || group.Count == actions.Count,
                    $"Guide row '{keys}': {group.Count} keys against {actions.Count} 3270 keys.");
                for (var i = 0; i < group.Count; i++)
                    Assert.True(keyClaims.TryAdd(group[i], actions.Count == 1 ? actions[0] : actions[i]),
                        $"The guide lists {ChordSyntax.Format(group[i])} twice.");
            }
        }

        var erasing = DefaultKeymap.Create(destructiveBackspace: true);
        var cursorLeft = DefaultKeymap.Create(destructiveBackspace: false);
        Assert.True(sawBackspace, "The guide's Keyboard table has no Backspace row.");
        Assert.Equal(Lines(erasing.Keys.Where(pair => pair.Key != Backspace)), Lines(keyClaims));
        Assert.Equal(TextLines(erasing.Text), TextLines(textClaims));
        // Backspace is the profile's choice, so the guide's one row stands for both defaults.
        Assert.Equal(TerminalKey.Erase, erasing.Keys[Backspace]);
        Assert.Equal(TerminalKey.Backspace, cursorLeft.Keys[Backspace]);
        Assert.Equal(Lines(erasing.Keys.Where(pair => pair.Key != Backspace)), Lines(cursorLeft.Keys.Where(pair => pair.Key != Backspace)));
    }

    private static IEnumerable<string> Lines(IEnumerable<KeyValuePair<KeyChord, TerminalKey>> pairs) =>
        pairs.Select(pair => $"{ChordSyntax.Format(pair.Key)} = {pair.Value}").Order(StringComparer.Ordinal);

    private static IEnumerable<string> TextLines(IEnumerable<KeyValuePair<KeyChord, string>> pairs) =>
        pairs.Select(pair => $"{ChordSyntax.Format(pair.Key)} types {pair.Value}").Order(StringComparer.Ordinal);

    /// <summary>The table's rows as (left cell, right cell), header and separator dropped.</summary>
    private static List<(string Keys, string Action)> Rows()
    {
        var lines = File.ReadAllText(GuidePath()).ReplaceLineEndings("\n").Split('\n');
        var start = Array.IndexOf(lines, "## Keyboard");
        Assert.True(start >= 0, "docs/user-guide.md has no '## Keyboard' section.");
        var rows = new List<(string, string)>();
        var inTable = false;
        foreach (var line in lines.Skip(start + 1))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal)) break;
            if (!line.StartsWith('|'))
            {
                if (inTable) break;
                continue;
            }
            inTable = true;
            var cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();
            if (cells[0] == "Key" || cells[0].StartsWith("---", StringComparison.Ordinal)) continue;
            rows.Add((cells[0], cells[1]));
        }
        Assert.NotEmpty(rows);
        return rows;
    }

    /// <summary>A left cell as groups of chords: each comma or "or" item is a group; a range ("F1 – F12"), a slash
    /// pair ("Tab / Shift+Tab") and "Arrow keys" are one group of several chords, paired position by position with
    /// the right cell.</summary>
    private static List<List<KeyChord>> Groups(string cell)
    {
        var items = cell.Replace(", or ", ", ")
            .Split([", ", " or "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return [.. items.Select(item => Group(item, cell))];
    }

    private static List<KeyChord> Group(string item, string row)
    {
        if (item == "Arrow keys") return [new(Key.Up), new(Key.Down), new(Key.Left), new(Key.Right)];
        if (item.Contains(" – ", StringComparison.Ordinal))
        {
            var ends = item.Split(" – ");
            var first = Chord(ends[0], row);
            var last = KeyOf(ends[1], row);
            return [.. Enumerable.Range((int)first.Key, (int)last - (int)first.Key + 1).Select(k => new KeyChord((Key)k, first.Modifiers))];
        }
        return [.. item.Split(" / ").Select(part => Chord(part, row))];
    }

    private static KeyChord Chord(string spelling, string row)
    {
        var text = spelling.Trim();
        if (text.StartsWith("a tap of ", StringComparison.OrdinalIgnoreCase))
        {
            return KeyChord.TapOf(text["a tap of ".Length..] switch
            {
                "Left Ctrl" => Key.LeftCtrl,
                "Right Ctrl" => Key.RightCtrl,
                var other => throw Unreadable(row, other),
            });
        }
        var parts = text.Split('+');
        var modifiers = KeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            modifiers |= part switch
            {
                "Ctrl" => KeyModifiers.Control,
                "Alt" => KeyModifiers.Alt,
                "Shift" => KeyModifiers.Shift,
                _ => throw Unreadable(row, part),
            };
        }
        return new KeyChord(KeyOf(parts[^1], row), modifiers);
    }

    private static Key KeyOf(string word, string row)
    {
        if (KeyWords.TryGetValue(word.Trim(), out var key)) return key;
        if (FunctionKey().Match(word.Trim()) is { Success: true } match) return Key.F1 + int.Parse(match.Groups[1].Value) - 1;
        throw Unreadable(row, word);
    }

    /// <summary>A right cell as 3270 keys, in order: a range, a slash pair, "Move the cursor", or one name.</summary>
    private static List<TerminalKey> Actions(string cell, string row)
    {
        if (cell == "Move the cursor") return [TerminalKey.Up, TerminalKey.Down, TerminalKey.Left, TerminalKey.Right];
        if (cell.Contains(" – ", StringComparison.Ordinal))
        {
            var ends = cell.Split(" – ").Select(name => NameOf(name, row)).ToArray();
            return [.. Enumerable.Range((int)ends[0], (int)ends[1] - (int)ends[0] + 1).Select(k => (TerminalKey)k)];
        }
        return [.. cell.Split(" / ").Select(name => NameOf(name, row))];
    }

    private static TerminalKey NameOf(string name, string row)
    {
        var text = name.Trim();
        return text switch
        {
            "Back Tab" => TerminalKey.BackTab,
            "Toggle insert mode" => TerminalKey.Insert,
            "Erase EOF" => TerminalKey.EraseEof,
            _ when text.All(char.IsAsciiLetterOrDigit) && Enum.TryParse<TerminalKey>(text, out var key) && Enum.IsDefined(key) => key,
            _ => throw Unreadable(row, text),
        };
    }

    private static InvalidOperationException Unreadable(string row, string what) =>
        new($"Guide Keyboard table, row '{row}': cannot read '{what}'. Extend UserGuideKeyboardTableTests' vocabulary or reword the row.");

    private static string GuidePath([CallerFilePath] string path = "")
    {
        var dir = Path.GetDirectoryName(path);
        while (dir is not null && !File.Exists(Path.Combine(dir, "LizTerm.slnx"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? throw new InvalidOperationException($"No LizTerm.slnx above '{path}'."), "docs", "user-guide.md");
    }
}
