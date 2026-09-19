// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.App.Keyboard;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

/// <summary>One action on the Keyboard tab (spec §5.3): its name, the chords that do it and the answer to a chord
/// the Add slot captured for it. Rows are created once and refreshed in place, so a control in a row (the slot)
/// survives every change.</summary>
public sealed class KeymapRow : ObservableObject
{
    private const string BackspaceNote =
        "The profile's Backspace setting decides which of these the Backspace key does, until you bind it here.";

    private readonly KeymapViewModel _keymap;
    private readonly Func<PlatformHotkeys> _hotkeys;
    private readonly IFormatProvider? _format;

    public KeymapRow(KeymapAction target, KeymapViewModel keymap, Func<PlatformHotkeys> hotkeys, IFormatProvider? format)
    {
        Target = target;
        _keymap = keymap;
        _hotkeys = hotkeys;
        _format = format;
        Title = TitleOf(target);
        AddName = "Add a key to " + Title;
        Note = target is KeymapAction.SendKey { Key: TerminalKey.Erase or TerminalKey.Backspace } ? BackspaceNote : null;
        Refresh();
    }

    public KeymapAction Target { get; }
    public string Title { get; }

    /// <summary>The slot's accessible name; "Add" alone is one of forty.</summary>
    public string AddName { get; }

    public string? Note { get; }
    public bool HasNote => Note is not null;
    public ObservableCollection<KeymapChip> Chips { get; } = [];

    /// <summary>The name a row, and a "Moved from" message, uses for an action.</summary>
    public static string TitleOf(KeymapAction action) => action switch
    {
        KeymapAction.SendKey send => send.Key switch
        {
            TerminalKey.BackTab => "Back Tab",
            TerminalKey.EraseEof => "Erase EOF",
            TerminalKey.EraseInput => "Erase Input",
            TerminalKey.FieldMark => "Field Mark",
            TerminalKey.Erase => "Backspace, erasing",
            TerminalKey.Backspace => "Backspace, moving left",
            var other => other.ToString(),
        },
        KeymapAction.TypeText type => "Type " + type.Text,
        _ => "Nothing",
    };

    /// <summary>Recomputes the chips from the model; a row whose chords did not change is left alone, so its
    /// buttons keep their focus.</summary>
    public void Refresh()
    {
        var chords = KeymapHints.Ordered(_keymap.ChordsFor(Target)).ToList();
        if (chords.SequenceEqual(Chips.Select(chip => chip.Chord))) return;
        Chips.Clear();
        foreach (var chord in chords) Chips.Add(new KeymapChip(chord, KeymapHints.Describe(chord, _format), _keymap.Unbind));
    }
}
