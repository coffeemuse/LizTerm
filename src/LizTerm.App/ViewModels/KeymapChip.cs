// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.Keyboard;

namespace LizTerm.App.ViewModels;

/// <summary>One chord on a Keyboard tab row: the text it reads as (KeymapHints' wording, so a chip matches the
/// keypad tooltip) and the command behind its × button.</summary>
public sealed class KeymapChip
{
    public KeymapChip(KeyChord chord, string text, Action<KeyChord> remove)
    {
        Chord = chord;
        Text = text;
        RemoveName = "Remove " + text;
        RemoveCommand = new RelayCommand(() => remove(chord));
    }

    public KeyChord Chord { get; }
    public string Text { get; }

    /// <summary>The × button's accessible name; the glyph alone says nothing to a screen reader.</summary>
    public string RemoveName { get; }

    public ICommand RemoveCommand { get; }
}
