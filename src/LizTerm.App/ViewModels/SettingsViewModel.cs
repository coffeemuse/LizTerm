// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.ComponentModel;
using LizTerm.Core.Settings;

namespace LizTerm.App.ViewModels;

/// <summary>The app-wide settings as one live object: every session window and the Preferences window bind to
/// the same instance, which is the whole of how a change in one reaches the others. One per process, owned by
/// App; a view model built without one gets an in-memory instance, which is what every test wants.
///
/// A setter that changes nothing returns early. One that does applies the change in memory first and raises
/// the property change, then writes through the store — the in-memory change always wins, because the UI
/// must show what the user chose even when the disk refuses it. A save that throws sets LastSaveError and
/// raises SaveFailed; the event fires on every failure, where a property whose text has not changed would not
/// notify a banner the user had dismissed. UI thread only: the menus and the window that set it are.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore? _store;
    private string? _lastSaveError;

    /// <summary>In-memory only: every default, nothing written.</summary>
    public SettingsViewModel() => Current = new AppSettings();

    /// <summary>Loaded from the store now, and written through on every change.</summary>
    public SettingsViewModel(SettingsStore store)
    {
        _store = store;
        Current = store.Load();
    }

    /// <summary>The record as this process sees it. Another process's later write is seen at the next launch;
    /// SettingsStore.Update keeps that process's keys, this object keeps its own view.</summary>
    public AppSettings Current { get; private set; }

    /// <summary>The last save's failure, or null once a save succeeds. The Preferences window shows it.</summary>
    public string? LastSaveError
    {
        get => _lastSaveError;
        private set => SetProperty(ref _lastSaveError, value);
    }

    /// <summary>Every failed save, with the message the banner shows.</summary>
    public event EventHandler<string>? SaveFailed;

    public CrosshairMode Crosshair
    {
        get => Current.Crosshair;
        set
        {
            if (Current.Crosshair != value) Apply(nameof(Crosshair), s => s with { Crosshair = value });
        }
    }

    public bool Blink
    {
        get => Current.Blink;
        set
        {
            if (Current.Blink != value) Apply(nameof(Blink), s => s with { Blink = value });
        }
    }

    public bool VisualBell
    {
        get => Current.VisualBell;
        set
        {
            if (Current.VisualBell != value) Apply(nameof(VisualBell), s => s with { VisualBell = value });
        }
    }

    public BellSound BellSound
    {
        get => Current.BellSound;
        set
        {
            if (Current.BellSound != value) Apply(nameof(BellSound), s => s with { BellSound = value });
        }
    }

    public bool Keypad
    {
        get => Current.Keypad;
        set
        {
            if (Current.Keypad != value) Apply(nameof(Keypad), s => s with { Keypad = value });
        }
    }

    public KeypadDock KeypadDock
    {
        get => Current.KeypadDock;
        set
        {
            if (Current.KeypadDock != value) Apply(nameof(KeypadDock), s => s with { KeypadDock = value });
        }
    }

    /// <summary>The keypad's two PF banks (#105). On by default, so an upgrade changes nobody's keypad; off leaves
    /// the third bank, the keys a keyboard with F-keys still has no key for.</summary>
    public bool KeypadPfKeys
    {
        get => Current.KeypadPfKeys;
        set
        {
            if (Current.KeypadPfKeys != value) Apply(nameof(KeypadPfKeys), s => s with { KeypadPfKeys = value });
        }
    }

    /// <summary>The session window's status bar draws the profile's tag chips (#93). Off by default: some people
    /// want PROD in sight at all times and others find the bar cluttered, and the bar as it was is the safer
    /// default for an upgrade. The note icon and the connect banner are not behind this; they cost no width.</summary>
    public bool ShowTagsInStatusBar
    {
        get => Current.ShowTagsInStatusBar;
        set
        {
            if (Current.ShowTagsInStatusBar != value) Apply(nameof(ShowTagsInStatusBar), s => s with { ShowTagsInStatusBar = value });
        }
    }

    public MenuStyle MenuStyle
    {
        get => Current.MenuStyle;
        set
        {
            // The unchanged-value guard every other setter has, plus the one case where "unchanged" still has to
            // write: a seeded style is in memory and not in the file, so the radio showing it is already checked
            // and clicking it is the user asking for it to become their preference. Without this the seeded
            // style is the one value they cannot save, and the next launch without LIZTERM_MENU loses it (#70).
            if (Current.MenuStyle == value && !_menuStyleSeeded) return;
            _menuStyleSeeded = false;
            Apply(nameof(MenuStyle), s => s with { MenuStyle = value });
        }
    }

    /// <summary>Whether MenuStyle currently holds a LIZTERM_MENU seed rather than anything the file says, which
    /// is what makes a click on the matching radio a write rather than a no-op.</summary>
    private bool _menuStyleSeeded;

    /// <summary>The style LIZTERM_MENU named, applied to this instance and nothing else: in memory, notifying so
    /// open windows follow it, and never written. App calls it once at startup. It cannot reach the file later
    /// either — SettingsStore.Update applies each change to the record it re-reads from disk, so a save the user
    /// triggers carries only the key they changed.</summary>
    internal void SeedMenuStyle(MenuStyle style)
    {
        _menuStyleSeeded = true;
        if (Current.MenuStyle == style) return;
        Current = Current with { MenuStyle = style };
        OnPropertyChanged(nameof(MenuStyle));
    }

    /// <summary>The notification keeps its place ahead of the save, so the value change always reaches the UI
    /// before any LastSaveError does — but it goes in a try/finally, because a subscriber now does real work in
    /// it: a session window rebuilds its menus on a MenuStyle change, and MenuLookup.Required throws for a menu
    /// missing a header it names. An exception stops the multicast delegate wherever it is raised; the finally at
    /// least keeps it from costing the user the write as well as the windows after it.</summary>
    private void Apply(string property, Func<AppSettings, AppSettings> change)
    {
        Current = change(Current);
        try
        {
            OnPropertyChanged(property);
        }
        finally
        {
            Save(change);
        }
    }

    private void Save(Func<AppSettings, AppSettings> change)
    {
        if (_store is null) return;
        try
        {
            _store.Update(change);
            LastSaveError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LastSaveError = "Could not save settings: " + ex.Message;
            SaveFailed?.Invoke(this, LastSaveError);
        }
    }
}
