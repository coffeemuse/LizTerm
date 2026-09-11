// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Views;

public class PreferencesWindowTests
{
    private static (PreferencesWindow Window, SettingsViewModel Settings) Show(SettingsViewModel? settings = null)
    {
        settings ??= new SettingsViewModel();
        var window = new PreferencesWindow(settings);
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

    [AvaloniaFact]
    public void It_is_titled_preferences_and_sizes_to_its_content()
    {
        var (window, _) = Show();

        Assert.Equal("Preferences", window.Title);
        Assert.Equal(SizeToContent.Height, window.SizeToContent);
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
}
