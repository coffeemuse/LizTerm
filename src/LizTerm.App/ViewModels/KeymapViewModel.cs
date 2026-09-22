// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.App.Keyboard;
using LizTerm.Core.Settings;

namespace LizTerm.App.ViewModels;

/// <summary>The user's keymap as one live object (editable keymap spec §5.1), one per process and owned by App as
/// SettingsViewModel is: every session window composes its map from it, and the Keyboard tab edits it. A change
/// applies in memory first and raises Changed, then writes through the store, so the in-memory change always wins;
/// a save that throws sets LastSaveError and raises SaveFailed, the settings object's contract, so the tab's banner
/// is the same banner. The row queries answer from the erasing default so a view never reads the dictionary; that
/// is the seam a chord-centric view would sit on. UI thread only: the windows and the tab that use it are.</summary>
public sealed class KeymapViewModel : ObservableObject
{
    /// <summary>What the tab and the launch notice add to the store's account of a file they cannot use: that the
    /// defaults are standing in, and that the file itself is untouched, which is what makes a repair worth trying
    /// before a reset.</summary>
    private const string UsingDefaults = "LizTerm is using the default keys until it is fixed; your file has not been changed.";

    private readonly KeymapStore? _store;
    private string? _lastSaveError;
    private string? _loadError;
    private bool _unsaved;
    private Keymap? _erasing;
    private Keymap? _cursorLeft;

    /// <summary>In-memory only: every default, nothing written.</summary>
    public KeymapViewModel() => Overlay = KeymapOverlay.Empty;

    /// <summary>Loaded from the store now, and written through on every change.</summary>
    public KeymapViewModel(KeymapStore store)
    {
        _store = store;
        var load = store.Load();
        Overlay = KeymapOverlay.Parse(load.File);
        _loadError = load.Problem is null ? null : load.Problem + " " + UsingDefaults;
    }

    /// <summary>The differences as this process sees them. Another process's later write is seen at the next
    /// launch; KeymapStore.Update keeps that process's entries, this object keeps its own view.</summary>
    internal KeymapOverlay Overlay { get; private set; }

    /// <summary>After every Bind, Unbind and ResetToDefaults, before the save.</summary>
    public event EventHandler? Changed;

    /// <summary>Every failed save, with the message the banner shows.</summary>
    public event EventHandler<string>? SaveFailed;

    /// <summary>The last save's failure, or null once a save succeeds.</summary>
    public string? LastSaveError
    {
        get => _lastSaveError;
        private set => SetProperty(ref _lastSaveError, value);
    }

    /// <summary>Why this build could not use keymap.json at all, or null when it could (#168). While it is set the
    /// overlay is empty, every default is in force, and every save would be refused by KeymapStore.Update, so the
    /// Keyboard tab shows this in place of the rows rather than an editor whose every change fails.</summary>
    public string? LoadError
    {
        get => _loadError;
        private set => SetProperty(ref _loadError, value);
    }

    /// <summary>Where the bindings live, for a message that has to name the file to fix, or null for an in-memory
    /// keymap, which has no file.</summary>
    public string? KeymapFilePath => _store?.FilePath;

    /// <summary>Entries in the file this build left alone, for the tab's note.</summary>
    public int UnreadableEntries => Overlay.IgnoredCount;

    /// <summary>Those same entries, named and explained, for the tab to list (#168).</summary>
    public IReadOnlyList<KeymapSkip> Skipped => Overlay.Skipped;

    /// <summary>The map in force for one window: the profile's Backspace choice under the user's entries. Composed
    /// once per change: Keymap is immutable, and the tab asks once per row on every change, so every caller shares
    /// the one instance until the next Apply.</summary>
    public Keymap Compose(bool destructiveBackspace) => destructiveBackspace
        ? _erasing ??= Overlay.Compose(destructiveBackspace: true)
        : _cursorLeft ??= Overlay.Compose(destructiveBackspace: false);

    /// <summary>Every chord that does <paramref name="action"/>, from the erasing default; none for Unbound.</summary>
    public IReadOnlyList<KeyChord> ChordsFor(KeymapAction action)
    {
        var map = Compose(destructiveBackspace: true);
        return action switch
        {
            KeymapAction.SendKey send => [.. map.Keys.Where(pair => pair.Value == send.Key).Select(pair => pair.Key)],
            KeymapAction.TypeText type => [.. map.Text.Where(pair => pair.Value == type.Text).Select(pair => pair.Key)],
            _ => [],
        };
    }

    /// <summary>What <paramref name="chord"/> does in the erasing default, or null when nothing.</summary>
    public KeymapAction? ActionOf(KeyChord chord)
    {
        var map = Compose(destructiveBackspace: true);
        if (map.TryMap(chord, out var key)) return new KeymapAction.SendKey(key);
        if (map.TryText(chord, out var text)) return new KeymapAction.TypeText(text);
        return null;
    }

    public void Bind(KeyChord chord, KeymapAction action) => Apply(overlay => overlay.Bind(chord, action));

    public void Unbind(KeyChord chord) => Apply(overlay => overlay.Unbind(chord));

    public void ResetToDefaults() => Apply(overlay => overlay.Cleared());

    /// <summary>The way out of a keymap.json this build cannot use (#168): the file goes aside under another name,
    /// so the next load finds none and every default applies, and this object is editable again. The bindings are
    /// kept rather than deleted, since a stray comma is a repair and not a reason to lose a hand-written file.
    /// A move that fails leaves the error standing and reports itself through LastSaveError, the banner every
    /// window already shows. Nothing to do when the file loaded.</summary>
    public void RestoreDefaults()
    {
        if (_store is null || LoadError is null) return;
        try
        {
            _store.MoveAside();
            LoadError = null;
            LastSaveError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastSaveError = "Could not reset the keymap: " + ex.Message;
            SaveFailed?.Invoke(this, LastSaveError);
        }
    }

    /// <summary>In memory first, then the notifications, then the write. The notifications sit in a try/finally for the reason SettingsViewModel's do: a subscriber that throws must not cost the user the save. The count is announced only when it moved.</summary>
    private void Apply(Func<KeymapOverlay, KeymapOverlay> change)
    {
        var before = UnreadableEntries;
        Overlay = change(Overlay);
        _erasing = _cursorLeft = null;
        try
        {
            if (UnreadableEntries != before) OnPropertyChanged(nameof(UnreadableEntries));
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            Save(change);
        }
    }

    /// <summary>The same change applied to what is on disk now, the discipline SettingsViewModel follows. After a
    /// failed save the disk no longer holds what this object shows, so the next save writes the whole overlay
    /// instead of one change to a file that lacks the earlier ones; the banner clears only when the two agree.</summary>
    private void Save(Func<KeymapOverlay, KeymapOverlay> change)
    {
        if (_store is null) return;
        try
        {
            if (_unsaved) _store.Update(_ => Overlay.ToFile());
            else _store.Update(file => change(KeymapOverlay.Parse(file)).ToFile());
            _unsaved = false;
            LastSaveError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _unsaved = true;
            LastSaveError = "Could not save the keymap: " + ex.Message;
            SaveFailed?.Invoke(this, LastSaveError);
        }
    }
}
