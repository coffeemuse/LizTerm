// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Input;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Keyboard;

/// <summary>The user's differences from the default (editable keymap spec §3), parsed: each chord to the action it
/// now has, or to Unbound. Immutable; Bind and Unbind answer a new overlay, pruned so the file stays sparse.
/// Entries this build cannot read (a chord that does not parse, a 3270 key name it does not know) ride along in
/// Ignored and go back to the file verbatim, the rule KeymapFile applies to values of the wrong JSON shape. Two
/// spellings of one chord in a file ("ctrl+home" and "Ctrl+Home") are one entry, the later winning.</summary>
public sealed class KeymapOverlay
{
    /// <summary>Sparseness is measured against this table (§3.1). Back is exempt because its default is the
    /// profile's choice, so a binding of it is a choice too, whichever value it has (§3.3).</summary>
    private static readonly Keymap Baseline = DefaultKeymap.Create(destructiveBackspace: true);
    private static readonly KeyChord Backspace = new(Key.Back);

    public static KeymapOverlay Empty { get; } = new(new Dictionary<KeyChord, KeymapAction>(), KeymapFile.Empty);

    public IReadOnlyDictionary<KeyChord, KeymapAction> Entries { get; }

    /// <summary>The file's entries this build left alone: unparsable chords and key names in Bindings, values of
    /// the wrong shape in Unreadable. Written back as they were.</summary>
    public KeymapFile Ignored { get; }

    public int IgnoredCount => Ignored.Bindings.Count + Ignored.Unreadable.Count;

    private KeymapOverlay(IReadOnlyDictionary<KeyChord, KeymapAction> entries, KeymapFile ignored)
    {
        Entries = entries;
        Ignored = ignored;
    }

    public static KeymapOverlay Parse(KeymapFile file)
    {
        var entries = new Dictionary<KeyChord, KeymapAction>();
        var ignored = new Dictionary<string, KeymapEntry>();
        foreach (var (spelling, entry) in file.Bindings)
        {
            if (ChordSyntax.TryParse(spelling, out var chord) && KeymapAction.TryFrom(entry, out var action))
                entries[chord] = action;
            else
                ignored[spelling] = entry;
        }
        return new(entries, new KeymapFile(ignored, file.Unreadable));
    }

    /// <summary>Entries in the canonical spelling, then the ignored ones. The two cannot collide: an ignored
    /// spelling is one that did not parse, and a canonical one always does.</summary>
    public KeymapFile ToFile() =>
        new(Entries.Select(e => KeyValuePair.Create(ChordSyntax.Format(e.Key), e.Value.ToEntry())).Concat(Ignored.Bindings),
            Ignored.Unreadable);

    /// <summary>With <paramref name="chord"/> doing <paramref name="action"/>; an entry that says what the
    /// baseline already says is dropped instead, Back excepted.</summary>
    public KeymapOverlay Bind(KeyChord chord, KeymapAction action)
    {
        var entries = new Dictionary<KeyChord, KeymapAction>(Entries);
        if (chord == Backspace || !MatchesBaseline(chord, action)) entries[chord] = action;
        else entries.Remove(chord);
        return new(entries, Ignored);
    }

    public KeymapOverlay Unbind(KeyChord chord) => Bind(chord, KeymapAction.Unbound.Instance);

    /// <summary>Everything gone, Ignored included: Reset to defaults is the user throwing the file away.</summary>
    public KeymapOverlay Cleared() => Empty;

    /// <summary>The map in force for a window: the profile's default, less every chord the user touched, plus the
    /// user's bindings. Removing first is what lets a chord move from the text table to the key table.</summary>
    public Keymap Compose(bool destructiveBackspace) =>
        DefaultKeymap.Create(destructiveBackspace)
            .Without(Entries.Keys)
            .With(
                Entries.Where(e => e.Value is KeymapAction.SendKey)
                    .Select(e => KeyValuePair.Create(e.Key, ((KeymapAction.SendKey)e.Value).Key)),
                Entries.Where(e => e.Value is KeymapAction.TypeText)
                    .Select(e => KeyValuePair.Create(e.Key, ((KeymapAction.TypeText)e.Value).Text)));

    private static bool MatchesBaseline(KeyChord chord, KeymapAction action) => action switch
    {
        KeymapAction.SendKey send => Baseline.TryMap(chord, out var key) && key == send.Key,
        KeymapAction.TypeText type => Baseline.TryText(chord, out var text) && text == type.Text,
        _ => !Baseline.TryMap(chord, out _) && !Baseline.TryText(chord, out _),
    };
}
