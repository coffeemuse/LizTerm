// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Diagnostics.CodeAnalysis;
using LizTerm.Core.Session;

namespace LizTerm.App.Keyboard;

/// <summary>A chord-to-key table plus a chord-to-text table (spec 6.1). Immutable. KeymapOverlay applies the user's
/// keymap.json over the default with <see cref="Without"/> then <see cref="With"/> (#18).</summary>
public sealed class Keymap
{
    private readonly Dictionary<KeyChord, TerminalKey> _keys;
    private readonly Dictionary<KeyChord, string> _text;

    public Keymap(IEnumerable<KeyValuePair<KeyChord, TerminalKey>> keys, IEnumerable<KeyValuePair<KeyChord, string>> text)
    {
        _keys = new Dictionary<KeyChord, TerminalKey>(keys);
        _text = new Dictionary<KeyChord, string>(text);
    }

    public IReadOnlyDictionary<KeyChord, TerminalKey> Keys => _keys;
    public IReadOnlyDictionary<KeyChord, string> Text => _text;

    public bool TryMap(KeyChord chord, out TerminalKey key) => _keys.TryGetValue(chord, out key);

    public bool TryText(KeyChord chord, [NotNullWhen(true)] out string? text) => _text.TryGetValue(chord, out text);

    /// <summary>A copy with the given entries added or replaced. Later entries win.</summary>
    public Keymap With(IEnumerable<KeyValuePair<KeyChord, TerminalKey>> keys, IEnumerable<KeyValuePair<KeyChord, string>> text)
    {
        var mergedKeys = new Dictionary<KeyChord, TerminalKey>(_keys);
        foreach (var (chord, key) in keys) mergedKeys[chord] = key;
        var mergedText = new Dictionary<KeyChord, string>(_text);
        foreach (var (chord, value) in text) mergedText[chord] = value;
        return new Keymap(mergedKeys, mergedText);
    }

    /// <summary>A copy with the given chords removed from both tables, so a user's unbinding takes a default away
    /// rather than shadowing it, and a chord can move from one table to the other.</summary>
    public Keymap Without(IEnumerable<KeyChord> chords)
    {
        var keys = new Dictionary<KeyChord, TerminalKey>(_keys);
        var text = new Dictionary<KeyChord, string>(_text);
        foreach (var chord in chords)
        {
            keys.Remove(chord);
            text.Remove(chord);
        }
        return new Keymap(keys, text);
    }
}
