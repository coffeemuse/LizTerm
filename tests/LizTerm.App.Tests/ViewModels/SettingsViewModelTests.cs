// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using LizTerm.App.ViewModels;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.ViewModels;

public class SettingsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static List<string> Changes(INotifyPropertyChanged source)
    {
        var names = new List<string>();
        source.PropertyChanged += (_, e) => names.Add(e.PropertyName!);
        return names;
    }

    [Fact]
    public void An_in_memory_instance_starts_at_every_default_and_raises_changes()
    {
        var settings = new SettingsViewModel();
        var changes = Changes(settings);
        Assert.Equal(new AppSettings(), settings.Current);

        settings.Crosshair = CrosshairMode.Both;
        settings.Blink = false;

        Assert.Equal(new AppSettings(CrosshairMode.Both, Blink: false), settings.Current);
        Assert.Equal(["Crosshair", "Blink"], changes);
        Assert.Null(settings.LastSaveError);
    }

    [Fact]
    public void The_same_value_again_raises_nothing_and_writes_nothing()
    {
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        var changes = Changes(settings);

        settings.Blink = true;
        settings.Crosshair = CrosshairMode.None;

        Assert.Empty(changes);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void A_store_backed_instance_writes_through_and_a_fresh_one_reads_it_back()
    {
        _ = new SettingsViewModel(new SettingsStore(FilePath)) { Crosshair = CrosshairMode.Vertical, Blink = false };

        var reloaded = new SettingsViewModel(new SettingsStore(FilePath));

        Assert.Equal(CrosshairMode.Vertical, reloaded.Crosshair);
        Assert.False(reloaded.Blink);
    }

    /// <summary>The in-memory change always wins: the UI must show what the user chose even when the disk
    /// refuses it. The failure is reported twice over — an event for banners that need every failure, a
    /// property for the window that shows the latest.</summary>
    [Fact]
    public void A_failed_save_keeps_the_change_and_reports_it()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "not json");
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        string? reported = null;
        settings.SaveFailed += (_, message) => reported = message;

        settings.Blink = false;

        Assert.False(settings.Blink);
        Assert.StartsWith("Could not save settings: ", reported);
        Assert.Contains(FilePath, reported);
        Assert.Equal(reported, settings.LastSaveError);
    }

    [Fact]
    public void The_next_successful_save_clears_the_error()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "not json");
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        settings.Blink = false;
        Assert.NotNull(settings.LastSaveError);
        var changes = Changes(settings);

        File.Delete(FilePath);
        settings.Crosshair = CrosshairMode.Both;

        Assert.Null(settings.LastSaveError);
        Assert.Equal(["Crosshair", "LastSaveError"], changes);
    }

    [Fact]
    public void A_failed_save_raises_the_error_property_change_after_the_value_change()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "not json");
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        var changes = Changes(settings);

        settings.Blink = false;

        Assert.Equal(["Blink", "LastSaveError"], changes);
    }

    [Fact]
    public void The_bell_settings_write_through_raise_their_own_names_and_skip_unchanged_values()
    {
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        var changes = Changes(settings);

        settings.VisualBell = true;                 // already the default: nothing
        settings.BellSound = BellSound.None;        // already the default: nothing
        Assert.Empty(changes);
        Assert.False(File.Exists(FilePath));

        settings.VisualBell = false;
        settings.BellSound = BellSound.SystemAlert;

        Assert.Equal(["VisualBell", "BellSound"], changes);
        var reloaded = new SettingsViewModel(new SettingsStore(FilePath));
        Assert.False(reloaded.VisualBell);
        Assert.Equal(BellSound.SystemAlert, reloaded.BellSound);
    }

    [Fact]
    public void The_keypad_properties_write_through_and_skip_unchanged_values()
    {
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        var changes = Changes(settings);

        settings.Keypad = false;
        settings.KeypadDock = KeypadDock.Bottom;
        Assert.Empty(changes);
        Assert.False(File.Exists(FilePath));

        settings.Keypad = true;
        settings.KeypadDock = KeypadDock.Right;

        Assert.Equal(["Keypad", "KeypadDock"], changes);
        var reloaded = new SettingsViewModel(new SettingsStore(FilePath));
        Assert.True(reloaded.Keypad);
        Assert.Equal(KeypadDock.Right, reloaded.KeypadDock);
    }

    [Fact]
    public void The_status_bar_tags_flag_writes_through_and_skips_an_unchanged_value()
    {
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        var changes = Changes(settings);

        settings.ShowTagsInStatusBar = false;
        Assert.Empty(changes);
        Assert.False(File.Exists(FilePath));

        settings.ShowTagsInStatusBar = true;

        Assert.Equal(["ShowTagsInStatusBar"], changes);
        Assert.True(new SettingsViewModel(new SettingsStore(FilePath)).ShowTagsInStatusBar);
    }

    [Fact]
    public void The_pf_keys_flag_writes_through_and_skips_an_unchanged_value()
    {
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        var changes = Changes(settings);

        settings.KeypadPfKeys = true;
        Assert.Empty(changes);
        Assert.False(File.Exists(FilePath));

        settings.KeypadPfKeys = false;

        Assert.Equal(["KeypadPfKeys"], changes);
        Assert.False(new SettingsViewModel(new SettingsStore(FilePath)).KeypadPfKeys);
    }

    [Fact]
    public void The_check_for_updates_flag_writes_through_and_skips_an_unchanged_value()
    {
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        var changes = Changes(settings);

        settings.CheckForUpdatesAutomatically = true;
        Assert.Empty(changes);
        Assert.False(File.Exists(FilePath));

        settings.CheckForUpdatesAutomatically = false;

        Assert.Equal(["CheckForUpdatesAutomatically"], changes);
        Assert.False(new SettingsViewModel(new SettingsStore(FilePath)).CheckForUpdatesAutomatically);
    }

    [Fact]
    public void The_splash_flag_writes_through_and_skips_an_unchanged_value()
    {
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        var changes = Changes(settings);

        settings.ShowSplashOnLaunch = true;
        Assert.Empty(changes);
        Assert.False(File.Exists(FilePath));

        settings.ShowSplashOnLaunch = false;

        Assert.Equal(["ShowSplashOnLaunch"], changes);
        Assert.False(new SettingsViewModel(new SettingsStore(FilePath)).ShowSplashOnLaunch);
    }

    /// <summary>Not bound to any Preferences control — App writes it from the Skip button's callback — but it
    /// follows the same write-through shape as every other setting.</summary>
    [Fact]
    public void The_skipped_update_version_writes_through_clears_and_skips_an_unchanged_value()
    {
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        var changes = Changes(settings);

        settings.SkippedUpdateVersion = null;
        Assert.Empty(changes);
        Assert.False(File.Exists(FilePath));

        settings.SkippedUpdateVersion = "0.6.0";

        Assert.Equal(["SkippedUpdateVersion"], changes);
        Assert.Equal("0.6.0", new SettingsViewModel(new SettingsStore(FilePath)).SkippedUpdateVersion);

        settings.SkippedUpdateVersion = null;

        Assert.Equal(["SkippedUpdateVersion", "SkippedUpdateVersion"], changes);
        Assert.Null(new SettingsViewModel(new SettingsStore(FilePath)).SkippedUpdateVersion);
    }

    [Fact]
    public void The_menu_style_writes_through_raises_its_own_name_and_skips_an_unchanged_value()
    {
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        var changes = Changes(settings);

        settings.MenuStyle = MenuStyle.Auto;        // already the default: nothing
        Assert.Empty(changes);
        Assert.False(File.Exists(FilePath));

        settings.MenuStyle = MenuStyle.Both;

        Assert.Equal(["MenuStyle"], changes);
        Assert.Equal(MenuStyle.Both, new SettingsViewModel(new SettingsStore(FilePath)).MenuStyle);
    }

    /// <summary>LIZTERM_MENU seeds the running instance and nothing else: the value applies in memory, notifies
    /// like any other change so open windows follow it, and never becomes the user's saved preference — not even
    /// by riding along with the next thing they do change.</summary>
    [Fact]
    public void A_seeded_menu_style_applies_in_memory_and_never_reaches_the_file()
    {
        var settings = new SettingsViewModel(new SettingsStore(FilePath));
        var changes = Changes(settings);

        settings.SeedMenuStyle(MenuStyle.Both);

        Assert.Equal(MenuStyle.Both, settings.MenuStyle);
        Assert.Equal(["MenuStyle"], changes);
        Assert.False(File.Exists(FilePath));

        settings.Blink = false;

        Assert.Equal(MenuStyle.Auto, new SettingsViewModel(new SettingsStore(FilePath)).MenuStyle);
    }
}
