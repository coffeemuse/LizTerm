// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.ObjectModel;
using Avalonia.Input;
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

    /// <summary>Unmodified Backspace is the one chord the sparse rule always keeps (spec §3.3), so capturing it
    /// onto its own default row is a real choice, not a no-op.</summary>
    private static readonly KeyChord BackspaceChord = new(Key.Back);

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
        CaptureHandler = TryCapture;
        Refresh();
    }

    public KeymapAction Target { get; }
    public string Title { get; }

    /// <summary>The slot's accessible name; "Add" alone is one of forty.</summary>
    public string AddName { get; }

    public string? Note { get; }
    public bool HasNote => Note is not null;
    public ObservableCollection<KeymapChip> Chips { get; } = [];

    /// <summary>What the Add slot calls with a chord it captured. Bound as a delegate so the control knows nothing
    /// about this class.</summary>
    public Func<KeyChord, CaptureResult> CaptureHandler { get; }

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

    /// <summary>Judges the chord (KeymapPolicy first: that is what keeps a Cmd chord out of the file), then binds
    /// it to this row's action. A chord another action held moves, and the answer says from where.</summary>
    public CaptureResult TryCapture(KeyChord chord)
    {
        if (KeymapPolicy.Check(chord, _hotkeys()) is KeymapVerdict.Refused refused) return new CaptureResult(false, refused.Reason);
        var previous = _keymap.ActionOf(chord);
        if (previous == Target && chord != BackspaceChord) return new CaptureResult(true, "Already does this");
        _keymap.Bind(chord, Target);
        return new CaptureResult(true, previous is null || previous == Target ? null : "Moved from " + TitleOf(previous));
    }
}
