// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Views;

public class PreferencesWindowTests
{
    /// <summary>The platform answer defaults to the full shape, the one that ships on macOS and Windows, so the
    /// radio tests exercise an enabled radio on every CI runner; the Linux shape is asked for by name.</summary>
    private static (PreferencesWindow Window, SettingsViewModel Settings) Show(
        SettingsViewModel? settings = null, bool systemAlertAvailable = true, bool menuStyleChoosable = true)
    {
        settings ??= new SettingsViewModel();
        var window = new PreferencesWindow(settings, systemAlertAvailable, menuStyleChoosable);
        window.Show();
        return (window, settings);
    }

    /// <summary>Raised through Button.ClickEvent, which is what the Click handlers listen to; assigning
    /// IsChecked would only prove that the one-way binding renders.</summary>
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void Clicking_a_crosshair_radio_sets_the_shared_settings_and_checks_exactly_that_radio()
    {
        var (window, settings) = Show();
        var radios = new[] { "CrosshairNone", "CrosshairHorizontal", "CrosshairVertical", "CrosshairBoth" }
            .Select(name => window.FindControl<RadioButton>(name)!).ToArray();
        var modes = new[] { CrosshairMode.None, CrosshairMode.Horizontal, CrosshairMode.Vertical, CrosshairMode.Both };
        Assert.Equal([true, false, false, false], radios.Select(r => r.IsChecked == true));

        for (var chosen = 0; chosen < modes.Length; chosen++)
        {
            Click(radios[chosen]);

            Assert.Equal(modes[chosen], settings.Crosshair);
            Assert.Equal(Enumerable.Range(0, modes.Length).Select(i => i == chosen), radios.Select(r => r.IsChecked == true));
        }
    }

    [AvaloniaFact]
    public void The_radios_follow_a_change_made_elsewhere()
    {
        var (window, settings) = Show();

        settings.Crosshair = CrosshairMode.Vertical;

        Assert.True(window.FindControl<RadioButton>("CrosshairVertical")!.IsChecked);
        Assert.False(window.FindControl<RadioButton>("CrosshairNone")!.IsChecked);
    }

    [AvaloniaFact]
    public void The_blink_box_writes_through_and_follows_the_settings()
    {
        var (window, settings) = Show();
        var box = window.FindControl<CheckBox>("BlinkBox")!;
        Assert.True(box.IsChecked);

        box.IsChecked = false;
        Assert.False(settings.Blink);

        settings.Blink = true;
        Assert.True(box.IsChecked);
    }

    [AvaloniaFact]
    public void The_visual_bell_box_writes_through_and_follows_the_settings()
    {
        var (window, settings) = Show();
        var box = window.FindControl<CheckBox>("VisualBellBox")!;
        Assert.True(box.IsChecked);

        box.IsChecked = false;
        Assert.False(settings.VisualBell);

        settings.VisualBell = true;
        Assert.True(box.IsChecked);
    }

    [AvaloniaFact]
    public void Clicking_a_sound_radio_sets_the_shared_settings_and_checks_exactly_that_radio()
    {
        var (window, settings) = Show();
        var none = window.FindControl<RadioButton>("BellSoundNone")!;
        var alert = window.FindControl<RadioButton>("BellSoundSystemAlert")!;
        Assert.True(none.IsChecked);
        Assert.False(alert.IsChecked);

        Click(alert);
        Assert.Equal(BellSound.SystemAlert, settings.BellSound);
        Assert.False(none.IsChecked);
        Assert.True(alert.IsChecked);

        Click(none);
        Assert.Equal(BellSound.None, settings.BellSound);
        Assert.True(none.IsChecked);
        Assert.False(alert.IsChecked);
    }

    [AvaloniaFact]
    public void The_sound_radios_follow_a_change_made_elsewhere()
    {
        var (window, settings) = Show();

        settings.BellSound = BellSound.SystemAlert;

        Assert.True(window.FindControl<RadioButton>("BellSoundSystemAlert")!.IsChecked);
        Assert.False(window.FindControl<RadioButton>("BellSoundNone")!.IsChecked);
    }

    /// <summary>The show box is here as well as in View so the group makes sense on its own (keypad spec §7).</summary>
    [AvaloniaFact]
    public void The_keypad_box_writes_through_and_follows_the_settings()
    {
        var (window, settings) = Show();
        var box = window.FindControl<CheckBox>("KeypadBox")!;
        Assert.False(box.IsChecked);

        box.IsChecked = true;
        Assert.True(settings.Keypad);

        settings.Keypad = false;
        Assert.False(box.IsChecked);
    }

    [AvaloniaFact]
    public void Clicking_a_dock_radio_sets_the_shared_settings_and_checks_exactly_that_radio()
    {
        var (window, settings) = Show();
        var bottom = window.FindControl<RadioButton>("KeypadBottom")!;
        var right = window.FindControl<RadioButton>("KeypadRight")!;
        Assert.True(bottom.IsChecked);
        Assert.False(right.IsChecked);

        Click(right);
        Assert.Equal(KeypadDock.Right, settings.KeypadDock);
        Assert.False(bottom.IsChecked);
        Assert.True(right.IsChecked);

        Click(bottom);
        Assert.Equal(KeypadDock.Bottom, settings.KeypadDock);
        Assert.True(bottom.IsChecked);
        Assert.False(right.IsChecked);
    }

    [AvaloniaFact]
    public void The_dock_radios_show_the_saved_value_on_open()
    {
        var (window, _) = Show(new SettingsViewModel { KeypadDock = KeypadDock.Right });

        Assert.True(window.FindControl<RadioButton>("KeypadRight")!.IsChecked);
        Assert.False(window.FindControl<RadioButton>("KeypadBottom")!.IsChecked);
    }

    /// <summary>On by default (#105), and only here rather than in View > Keypad: it is set once, where showing the
    /// keypad is flipped while working.</summary>
    [AvaloniaFact]
    public void The_pf_keys_box_writes_through_and_follows_the_settings()
    {
        var (window, settings) = Show();
        var box = window.FindControl<CheckBox>("KeypadPfKeysBox");
        Assert.NotNull(box);
        Assert.True(box.IsChecked);

        box.IsChecked = false;
        Assert.False(settings.KeypadPfKeys);

        settings.KeypadPfKeys = true;
        Assert.True(box.IsChecked);
    }

    /// <summary>Linux: the radio is disabled and says why, but a saved SystemAlert (a file exported from a Mac, one
    /// day) still shows as the value it is and is not rewritten (bell spec §5).</summary>
    [AvaloniaFact]
    public void Where_the_system_alert_is_unavailable_the_radio_is_disabled_with_a_note_and_a_saved_value_stays()
    {
        var (window, settings) = Show(new SettingsViewModel { BellSound = BellSound.SystemAlert }, systemAlertAvailable: false);
        var alert = window.FindControl<RadioButton>("BellSoundSystemAlert")!;
        var note = window.FindControl<TextBlock>("BellSoundNote")!;

        Assert.False(alert.IsEnabled);
        Assert.True(alert.IsChecked);
        Assert.True(note.IsVisible);
        Assert.Equal("The system alert sound is not available on Linux.", note.Text);
        Assert.Equal(BellSound.SystemAlert, settings.BellSound);
    }

    [AvaloniaFact]
    public void Where_the_system_alert_is_available_the_radio_is_enabled_and_the_note_hidden()
    {
        var (window, _) = Show(systemAlertAvailable: true);

        Assert.True(window.FindControl<RadioButton>("BellSoundSystemAlert")!.IsEnabled);
        Assert.False(window.FindControl<TextBlock>("BellSoundNote")!.IsVisible);
    }

    [AvaloniaFact]
    public void A_failed_save_shows_its_message_in_the_window()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lizterm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "settings.json");
        File.WriteAllText(file, "not json");
        try
        {
            var (window, _) = Show(new SettingsViewModel(new SettingsStore(file)));
            var text = window.FindControl<TextBlock>("SaveErrorText")!;
            Assert.True(string.IsNullOrEmpty(text.Text));

            window.FindControl<CheckBox>("BlinkBox")!.IsChecked = false;

            Assert.StartsWith("Could not save settings: ", text.Text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Done_closes_the_window()
    {
        var (window, _) = Show();

        Click(window.FindControl<Button>("DoneButton")!);

        Assert.False(window.IsVisible);
    }

    /// <summary>A fixed size rather than SizeToContent: only the selected tab is measured, so a window sized to
    /// its content would change height on every tab switch.</summary>
    [AvaloniaFact]
    public void It_is_titled_preferences_and_has_a_fixed_size()
    {
        var (window, _) = Show();

        Assert.Equal("Preferences", window.Title);
        Assert.Equal(SizeToContent.Manual, window.SizeToContent);
        Assert.False(window.CanResize);
        Assert.Equal(520, window.Width);
        Assert.Equal(446, window.Height);
    }

    /// <summary>The fixed height has to hold the tallest tab: a row past it is cut off under the Done bar, and there
    /// is no scroll bar to reach it. Found in the running app for #105, whose PF keys box was cut in half.</summary>
    [AvaloniaFact]
    public void Every_tab_fits_the_fixed_size()
    {
        var (window, _) = Show();
        var tabs = window.FindControl<TabControl>("Tabs")!;

        for (var i = 0; i < tabs.ItemCount; i++)
        {
            tabs.SelectedIndex = i;
            window.UpdateLayout();
            var rows = (Panel)((TabItem)tabs.SelectedItem!).Content!;
            var last = rows.Children.Last(row => row.IsVisible);
            var bottom = last.TranslatePoint(new Point(0, last.Bounds.Height), tabs)!.Value.Y;

            Assert.True(bottom <= tabs.Bounds.Height, $"tab {i} ends at {bottom}, below the tab control's {tabs.Bounds.Height}");
        }
    }

    /// <summary>The tabs are the window's structure: General first (#107 — not about the screen, the bell, or
    /// the window's own chrome), then Display, Bell and Window in that order, the last read top of the window to
    /// bottom (menu bar, status bar, keypad). Every control keeps its name, so the other tests here find it
    /// whichever tab is selected.</summary>
    [AvaloniaFact]
    public void The_settings_sit_on_general_display_bell_and_window_tabs_in_that_order()
    {
        var (window, _) = Show();
        var tabs = window.FindControl<TabControl>("Tabs")!;

        Assert.Equal(["General", "Display", "Bell", "Window"], tabs.Items.Cast<TabItem>().Select(t => (string)t.Header!));
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.Same(window.FindControl<RadioButton>("CrosshairNone"), tabs.Items.Cast<TabItem>().ElementAt(1).FindLogicalDescendantOfType<RadioButton>());
    }

    [AvaloniaFact]
    public void Checking_the_check_for_updates_box_sets_the_shared_settings()
    {
        var (window, settings) = Show();
        var box = window.FindControl<CheckBox>("CheckForUpdatesBox")!;
        Assert.True(box.IsChecked);

        box.IsChecked = false;
        Assert.False(settings.CheckForUpdatesAutomatically);

        settings.CheckForUpdatesAutomatically = true;
        Assert.True(box.IsChecked);
    }

    /// <summary>App's one-at-a-time rule, through the internal seam that takes a settings object so the test
    /// never touches the real settings file. The public ShowPreferences() uses App.Settings.</summary>
    [AvaloniaFact]
    public void The_app_shows_one_preferences_window_at_a_time()
    {
        var app = (App)Application.Current!;
        var settings = new SettingsViewModel();

        var first = app.ShowPreferences(settings);
        Assert.Same(first, app.ShowPreferences(settings));

        first.Close();
        var again = app.ShowPreferences(settings);
        Assert.NotSame(first, again);
        again.Close();
    }

    /// <summary>The same one-way-plus-Click shape as the crosshair radios, over an untouched SettingsViewModel —
    /// the state of every install that has never named a style, since App seeds only when LIZTERM_MENU names one.
    /// Auto gets a radio of its own for exactly that reason: it is what the file reads as, so without one the
    /// whole group would open with nothing checked, and nothing would lead back to following the platform.</summary>
    [AvaloniaFact]
    public void Clicking_a_menu_style_radio_sets_the_shared_settings_and_checks_exactly_that_radio()
    {
        var (window, settings) = Show();
        var radios = new[] { "MenuStyleAuto", "MenuStyleNative", "MenuStyleInWindow", "MenuStyleBoth" }
            .Select(name => window.FindControl<RadioButton>(name)!).ToArray();
        var styles = new[] { MenuStyle.Auto, MenuStyle.Native, MenuStyle.InWindow, MenuStyle.Both };
        Assert.Equal([true, false, false, false], radios.Select(r => r.IsChecked == true));

        for (var chosen = 0; chosen < styles.Length; chosen++)
        {
            Click(radios[chosen]);

            Assert.Equal(styles[chosen], settings.MenuStyle);
            Assert.Equal(Enumerable.Range(0, styles.Length).Select(i => i == chosen), radios.Select(r => r.IsChecked == true));
        }
    }

    /// <summary>A seeded style is already the checked radio, so clicking it would be a no-op under the guard every
    /// other setter has — and the seed is in memory only, so the user's click would never reach the file and the
    /// next launch without the variable would lose it. The seeded style must be savable like any other.</summary>
    [AvaloniaFact]
    public void Clicking_the_radio_a_seed_already_checked_still_records_the_choice()
    {
        var settings = new SettingsViewModel();
        settings.SeedMenuStyle(MenuStyle.InWindow);
        var (window, _) = Show(settings);
        var seeded = window.FindControl<RadioButton>("MenuStyleInWindow")!;
        Assert.True(seeded.IsChecked);
        var changes = new List<string?>();
        settings.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Click(seeded);

        Assert.Equal([nameof(SettingsViewModel.MenuStyle)], changes);
        Assert.Equal(MenuStyle.InWindow, settings.MenuStyle);
    }

    /// <summary>Windows and Linux draw both renderers inside the window, where "Both" would be two stacked bars
    /// and "in the system menu bar" names nowhere. Hidden rather than disabled: there is no choice to explain.</summary>
    [AvaloniaFact]
    public void The_menu_style_group_is_shown_only_where_the_choice_means_something()
    {
        var (mac, _) = Show(menuStyleChoosable: true);
        Assert.True(mac.FindControl<Control>("MenuStyleGroup")!.IsVisible);

        var (elsewhere, _) = Show(menuStyleChoosable: false);
        Assert.False(elsewhere.FindControl<Control>("MenuStyleGroup")!.IsVisible);
    }

    /// <summary>The Status bar group (#93): one box, two-way, live like everything else here.</summary>
    [AvaloniaFact]
    public void The_status_bar_tags_box_writes_through_and_follows_the_settings()
    {
        var (window, settings) = Show();
        var box = window.FindControl<CheckBox>("StatusBarTagsBox")!;
        Assert.False(box.IsChecked);

        box.IsChecked = true;
        Assert.True(settings.ShowTagsInStatusBar);

        settings.ShowTagsInStatusBar = false;
        Assert.False(box.IsChecked);
    }
}
