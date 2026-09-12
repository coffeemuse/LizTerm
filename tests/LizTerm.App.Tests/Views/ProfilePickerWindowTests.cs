// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Views;

public class ProfilePickerWindowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-picker-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>The picker's Connect button is IsDefault, so without the box consuming Enter, typing a host and
    /// pressing Enter would connect the SELECTED PROFILE instead — a different host entirely. Handled at the box
    /// means the window-level default button never sees the key, the same shape SessionWindow.OnFindBoxKeyDown
    /// uses to keep the find bar's typing off the wire (spec 7.3).</summary>
    [AvaloniaFact]
    public void Enter_in_the_quick_connect_box_connects_the_typed_host_not_the_selected_profile()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "saved", Host = "saved.host", Port = 23 });
        SessionProfile? opened = null;
        var window = new ProfilePickerWindow(store, (p, _) => opened = p, () => { });
        window.Show();

        var vm = (ProfilePickerViewModel)window.DataContext!;
        vm.SelectedRow = vm.VisibleRows.Single();
        Assert.True(vm.ConnectCommand.CanExecute(null));

        var box = window.FindControl<TextBox>("QuickConnectBox")!;
        vm.QuickConnectText = "other.example:3270";
        box.Focus();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.NotNull(opened);
        Assert.Equal("other.example", opened.Host);
        Assert.Equal(3270, opened.Port);
    }

    /// <summary>The row used to be a horizontal StackPanel holding a Width="300" box, which overflowed below about
    /// 460px — and the window's own MinWidth is 400, so a user could reach it just by dragging the corner in.
    /// Measured at MinWidth, where the row is 368px wide.</summary>
    [AvaloniaFact]
    public void The_quick_connect_row_fits_at_the_windows_minimum_width()
    {
        var store = new ProfileStore(_dir);
        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { });
        window.Width = window.MinWidth;
        window.Show();

        var box = window.FindControl<TextBox>("QuickConnectBox")!;
        var button = window.FindControl<Button>("QuickConnectButton")!;
        var row = (Panel)box.Parent!;

        // Measured against the row's own width, so the assertion is about the layout rather than about which
        // panel type holds it.
        Assert.True(box.Bounds.Width > 0, "the quick connect box collapsed to nothing");
        Assert.True(button.Bounds.Right <= row.Bounds.Width + 0.5,
            $"the Quick Connect button ends at {button.Bounds.Right} in a {row.Bounds.Width} row");
        Assert.True(box.Bounds.Right <= button.Bounds.Left + 0.5,
            $"the box ends at {box.Bounds.Right} and the button starts at {button.Bounds.Left}");
    }

    /// <summary>Walks the realised row rather than the view model, so the template's own bindings are what is
    /// asserted: a correct ProfileRow bound to a broken DataTemplate renders nothing and passes every view
    /// model test.</summary>
    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (var child in root.GetVisualDescendants().OfType<T>()) yield return child;
    }

    [AvaloniaFact]
    public void A_tagged_profile_draws_a_star_uppercase_chips_and_a_note()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile
        {
            Name = "mvsce", Host = "10.42.37.209", Port = 3270,
            Tags = TagSet.From(["FAVORITE", "prod", "mvs"]), Note = "no live data",
        });

        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { });
        window.Show();
        window.UpdateLayout();

        var list = Descendants<ListBox>(window).Single();
        var item = Descendants<ListBoxItem>(list).Single();
        var texts = Descendants<TextBlock>(item).Select(t => t.Text).ToList();

        Assert.Contains("mvsce", texts);
        Assert.Contains("10.42.37.209:3270", texts);
        Assert.Contains("no live data", texts);
        Assert.Contains("PROD", texts);
        Assert.Contains("MVS", texts);
        Assert.DoesNotContain("FAVORITE", texts);
        Assert.Contains(Descendants<TextBlock>(item), t => t.Name == "StarGlyph" && t.IsVisible);
    }

    [AvaloniaFact]
    public void An_untagged_un_noted_profile_draws_neither_a_star_nor_a_third_line()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "tk5", Host = "tk5.local", Port = 3270 });

        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { });
        window.Show();
        window.UpdateLayout();

        var item = Descendants<ListBoxItem>(window).Single();
        Assert.DoesNotContain(Descendants<TextBlock>(item), t => t.Name == "StarGlyph" && t.IsVisible);
        Assert.DoesNotContain(Descendants<TextBlock>(item), t => t.Name == "NoteLine" && t.IsVisible);
    }

    [AvaloniaFact]
    public void A_fourth_tag_collapses_into_an_overflow_chip_carrying_the_rest_in_its_tooltip()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile
        {
            Name = "vm-dev", Host = "vm.example.org", Port = 992,
            Tags = TagSet.From(["DEV", "PROD", "MVS", "TLS", "LAB"]),
        });

        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { });
        window.Show();
        window.UpdateLayout();

        var item = Descendants<ListBoxItem>(window).Single();
        var overflow = Descendants<TextBlock>(item).Single(t => t.Name == "OverflowChip");
        Assert.True(overflow.IsVisible);
        Assert.Equal("+2", overflow.Text);
        Assert.Equal("TLS, LAB", ToolTip.GetTip(overflow));
    }

    /// <summary>The list is 356px of a 520px window, and the row has to survive the 400px MinWidth too — the
    /// same measurement the quick connect row already carries, for the same reason.</summary>
    [AvaloniaFact]
    public void A_three_chip_row_fits_at_the_windows_minimum_width()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile
        {
            Name = "a-rather-long-profile-name", Host = "10.42.37.209", Port = 3270,
            Tags = TagSet.From(["FAVORITE", "PROD", "MVS", "TEST"]),
        });

        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { });
        window.Width = window.MinWidth;
        window.Show();
        window.UpdateLayout();

        var item = Descendants<ListBoxItem>(window).Single();
        foreach (var chip in Descendants<Border>(item).Where(b => b.Name == "Chip"))
        {
            Assert.True(chip.Bounds.Width > 0, "a chip collapsed to nothing");
            Assert.True(chip.Bounds.Right <= item.Bounds.Width + 0.5,
                $"a chip ends at {chip.Bounds.Right} in a {item.Bounds.Width} row");
        }
    }

    [AvaloniaFact]
    public void The_filter_row_narrows_the_list()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "alpha", Host = "h" });
        store.Save(new SessionProfile { Name = "zeta", Host = "h" });

        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { });
        window.Show();
        window.UpdateLayout();

        var box = window.FindControl<TextBox>("FilterBox")!;
        box.Text = "zet";
        window.UpdateLayout();

        Assert.Equal(["zeta"], Descendants<ListBoxItem>(window).Select(i => ((ProfileRow)i.DataContext!).Name));
        Assert.NotNull(window.FindControl<ComboBox>("ScopeBox"));
    }
}
