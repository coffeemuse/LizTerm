// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.App.Keyboard;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

/// <summary>The Keyboard tab's data (editable keymap spec §5.3): a row per action over KeymapViewModel's queries.
/// It never reads the keymap's dictionary; that is the seam a chord-centric view would sit on. Subscribed to the
/// process's KeymapViewModel for as long as the tab is open: Dispose it when the window closes. UI thread only.</summary>
public sealed class KeymapEditorViewModel : ObservableObject, IDisposable
{
    /// <summary>The spec's order. Both Backspace actions sit where the spec's one Backspace row does.</summary>
    private static readonly TerminalKey[] Order =
    [
        TerminalKey.Enter, TerminalKey.Newline, TerminalKey.Clear, TerminalKey.Reset, TerminalKey.Attn, TerminalKey.SysReq,
        .. Enumerable.Range((int)TerminalKey.PF1, 24).Select(i => (TerminalKey)i),
        .. Enumerable.Range((int)TerminalKey.PA1, 3).Select(i => (TerminalKey)i),
        TerminalKey.Tab, TerminalKey.BackTab, TerminalKey.Insert, TerminalKey.Home, TerminalKey.EraseEof,
        TerminalKey.EraseInput, TerminalKey.Delete, TerminalKey.Erase, TerminalKey.Backspace, TerminalKey.Dup,
        TerminalKey.FieldMark, TerminalKey.Up, TerminalKey.Down, TerminalKey.Left, TerminalKey.Right,
    ];

    private readonly KeymapViewModel _keymap;
    private readonly Func<PlatformHotkeys> _hotkeys;
    private readonly IFormatProvider? _format;
    private readonly HashSet<string> _seenText = [];

    /// <param name="hotkeys">The platform's reserved gestures, asked at each capture: the view passes a function over
    /// the window's platform settings, a test passes PlatformHotkeys.MacOS or Fallback.</param>
    /// <param name="format">Passed to KeymapHints for the chips; null is the platform's own words.</param>
    public KeymapEditorViewModel(KeymapViewModel keymap, Func<PlatformHotkeys> hotkeys, IFormatProvider? format = null)
    {
        _keymap = keymap;
        _hotkeys = hotkeys;
        _format = format;
        Rows = new ObservableCollection<KeymapRow>(Order.Select(key => NewRow(new KeymapAction.SendKey(key))));
        AddNewTextRows();
        keymap.Changed += OnKeymapChanged;
    }

    public ObservableCollection<KeymapRow> Rows { get; }

    public void Dispose() => _keymap.Changed -= OnKeymapChanged;

    private KeymapRow NewRow(KeymapAction target) => new(target, _keymap, _hotkeys, _format);

    private void OnKeymapChanged(object? sender, EventArgs e)
    {
        foreach (var row in Rows) row.Refresh();
        AddNewTextRows();
    }

    /// <summary>One "Type ¬" row per text action the composed map holds, and, once shown, for as long as the tab
    /// is open: removing the last chord must leave the row so Add can undo it. Sorted so the order does not depend
    /// on the map's dictionary.</summary>
    private void AddNewTextRows()
    {
        var texts = _keymap.Compose(destructiveBackspace: true).Text.Values.Distinct().Order(StringComparer.Ordinal);
        foreach (var text in texts)
            if (_seenText.Add(text)) Rows.Add(NewRow(new KeymapAction.TypeText(text)));
    }
}
