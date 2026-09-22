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
    /// the wrong shape in Unreadable. Written back as they were, unless ToFile finds the user has since bound that
    /// chord.</summary>
    public KeymapFile Ignored { get; }

    public int IgnoredCount => Ignored.Bindings.Count + Ignored.Unreadable.Count;

    /// <summary>The same entries, named and explained for the Keyboard tab (#168), in the file's own spellings and
    /// in ordinal order, so the list does not move about between launches.</summary>
    public IReadOnlyList<KeymapSkip> Skipped =>
        [.. Ignored.Bindings.Select(pair => Describe(pair.Key, pair.Value))
             .Concat(Ignored.Unreadable.Keys.Select(chord => new KeymapSkip(chord, KeymapSkipReason.WrongShape)))
             .OrderBy(skip => skip.Chord, StringComparer.Ordinal)];

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

    /// <summary>Entries in the canonical spelling, then the ignored ones, except any whose spelling the entries
    /// already use or names a chord the entries hold: a chord that parsed but whose action did not is ignored under
    /// a spelling that can collide with the canonical one, and a chord whose spelling never parses back (a Cmd
    /// chord, which the tab refuses but a caller could bind) would otherwise be written twice. ToFile never throws,
    /// whatever was bound.</summary>
    public KeymapFile ToFile()
    {
        var formatted = Entries.ToDictionary(e => ChordSyntax.Format(e.Key), e => e.Value.ToEntry());
        var carried = Ignored.Bindings.Where(pair =>
            !formatted.ContainsKey(pair.Key)
            && !(ChordSyntax.TryParse(pair.Key, out var chord) && Entries.ContainsKey(chord)));
        return new KeymapFile(formatted.Concat(carried), Ignored.Unreadable);
    }

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

    /// <summary>Why Parse passed this entry over. The chord is judged first, because it is what the user reads
    /// first: an entry with both halves wrong is reported as the chord it could not place, and its key name is not
    /// carried, since nothing yet says that name is wrong.</summary>
    private static KeymapSkip Describe(string spelling, KeymapEntry entry) =>
        !ChordSyntax.TryParse(spelling, out _) ? new(spelling, KeymapSkipReason.UnknownChord)
        : entry is KeymapEntry.SendKey send ? new(spelling, KeymapSkipReason.UnknownKey, send.KeyName)
        : new(spelling, KeymapSkipReason.EmptyText);

    private static bool MatchesBaseline(KeyChord chord, KeymapAction action) => action switch
    {
        KeymapAction.SendKey send => Baseline.TryMap(chord, out var key) && key == send.Key,
        KeymapAction.TypeText type => Baseline.TryText(chord, out var text) && text == type.Text,
        _ => !Baseline.TryMap(chord, out _) && !Baseline.TryText(chord, out _),
    };
}
