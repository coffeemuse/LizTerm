// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Views;

public class ManageTagsWindowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-managetags-window-" + Guid.NewGuid().ToString("N"));
    private readonly ProfileStore _profiles;
    private readonly TagRegistryStore _tags;

    public ManageTagsWindowTests()
    {
        _profiles = new ProfileStore(Path.Combine(_dir, "profiles"));
        _tags = new TagRegistryStore(Path.Combine(_dir, "tags.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private (ManageTagsWindow Window, ManageTagsViewModel Vm) Show(bool atMinimumWidth = false)
    {
        _profiles.Save(new SessionProfile { Name = "gateway", Host = "h", Tags = TagSet.From(["PROD", "TLS"]) });
        _profiles.Save(new SessionProfile { Name = "mvsce", Host = "h", Tags = TagSet.From(["FAVORITE", "PROD"]) });
        _tags.Save(new TagRegistry([new TagDefinition("PROD", TagColor.Amber), new TagDefinition("TLS", TagColor.Green)]));
        var vm = new ManageTagsViewModel(new TagMaintenance(_profiles, _tags));
        var window = new ManageTagsWindow(vm);
        if (atMinimumWidth) window.Width = window.MinWidth;
        window.Show();
        window.UpdateLayout();
        return (window, vm);
    }

    /// <summary>Walks the realised controls, so the template's own bindings are what is asserted.</summary>
    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual => root.GetVisualDescendants().OfType<T>();

    private static void Select(ManageTagsWindow window, ManageTagsViewModel vm, string name)
    {
        vm.SelectedRow = vm.Rows.Single(r => r.Name == name);
        window.UpdateLayout();
    }

    [AvaloniaFact]
    public void The_list_draws_the_star_and_uppercase_chips_with_their_counts()
    {
        var (window, _) = Show();

        var list = window.FindControl<ListBox>("TagList")!;
        var texts = Descendants<TextBlock>(list).Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();

        Assert.Contains("★", texts);
        Assert.Contains("FAVORITE", texts);
        Assert.Contains("PROD", texts);
        Assert.Contains("TLS", texts);
        Assert.Contains("2", texts);
        Assert.Equal(2, Descendants<Border>(list).Count(b => b.Name == "Chip" && b.IsEffectivelyVisible));
    }

    [AvaloniaFact]
    public void Nothing_selected_shows_the_hint_and_no_panel()
    {
        var (window, _) = Show();

        Assert.True(window.FindControl<TextBlock>("EmptyHint")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<DockPanel>("TagPanel")!.IsEffectivelyVisible);
    }

    /// <summary>Done is deliberately not the default button, and the box handles Enter itself, so a rename can never
    /// close the window instead.</summary>
    [AvaloniaFact]
    public void Enter_in_the_name_box_renames_and_leaves_the_window_open()
    {
        var (window, vm) = Show();
        Select(window, vm, "TLS");
        var box = window.FindControl<TextBox>("NameBox")!;
        box.Text = "SSL";
        box.Focus();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal(["PROD", "SSL"], _profiles.Load("gateway")!.Tags.Names);
        Assert.True(window.IsVisible);
    }

    [AvaloniaFact]
    public void A_swatch_click_recolours_the_tag()
    {
        var (window, vm) = Show();
        Select(window, vm, "TLS");
        var teal = Descendants<Button>(window.FindControl<ItemsControl>("SwatchList")!)
            .Single(b => ToolTip.GetTip(b) as string == "Teal");

        teal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(TagColor.Teal, _tags.Load().ColorOf("TLS"));
        Assert.Equal("TLS", vm.SelectedRow?.Name);
    }

    [AvaloniaFact]
    public void The_reserved_row_shows_its_profiles_but_no_rename_swatches_or_delete()
    {
        var (window, vm) = Show();

        Select(window, vm, "FAVORITE");

        Assert.True(window.FindControl<TextBox>("ReservedNameBox")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<Button>("RenameButton")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<Button>("DeleteButton")!.IsEffectivelyVisible);
        Assert.Empty(Descendants<Button>(window.FindControl<ItemsControl>("SwatchList")!));
        Assert.Contains(Descendants<TextBlock>(window.FindControl<ItemsControl>("UsedByList")!), t => t.Text == "mvsce");
    }

    [AvaloniaFact]
    public void Delete_puts_the_confirmation_strip_in_the_buttons_place()
    {
        var (window, vm) = Show();
        Select(window, vm, "TLS");

        vm.DeleteCommand.Execute(null);
        window.UpdateLayout();

        Assert.True(window.FindControl<Border>("ConfirmationStrip")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<Button>("DeleteButton")!.IsEffectivelyVisible);
        Assert.Equal("Delete TLS? It is removed from gateway.", window.FindControl<TextBlock>("ConfirmationText")!.Text);
    }

    /// <summary>The panel gets 276px at the window's 520px MinWidth, once the margins, the 200px list and the gap
    /// take theirs; seven 22px swatches and the name row must both fit in it.</summary>
    [AvaloniaFact]
    public void The_panel_fits_at_the_windows_minimum_width()
    {
        var (window, vm) = Show(atMinimumWidth: true);
        Select(window, vm, "PROD");

        var rename = window.FindControl<Button>("RenameButton")!;
        var lastSwatch = Descendants<Button>(window.FindControl<ItemsControl>("SwatchList")!).Last();
        foreach (var control in new Control[] { rename, lastSwatch })
        {
            var right = control.TranslatePoint(new Point(control.Bounds.Width, 0), window)!.Value.X;
            Assert.True(right <= window.Bounds.Width - 16 + 0.5,
                $"{control.Name ?? "the last swatch"} ends at {right} in a {window.Bounds.Width}px window");
        }
    }

    /// <summary>Review finding: at the default window size, with a failure message up from a partial rename AND
    /// the merge confirmation strip up over it, the fill StackPanel neither scrolls nor clips, so its content
    /// draws past the Delete row and the strip instead of yielding to them. The fill region must scroll instead.
    /// Reproduces the review's own retry flow: a partial rename DEV -&gt; TEST over 4 carriers (one blocked), then
    /// reselecting DEV and renaming to TEST again, which is now a merge because the partial rename already
    /// defined TEST.</summary>
    [AvaloniaFact]
    public void The_panel_scrolls_rather_than_drawing_over_the_strip_when_a_failure_and_a_merge_are_both_up()
    {
        for (var i = 0; i < 4; i++)
            _profiles.Save(new SessionProfile { Name = $"p{i}", Host = "h", Tags = TagSet.From(["DEV"]) });
        _tags.Save(new TagRegistry([new TagDefinition("DEV", TagColor.Teal)]));
        Directory.CreateDirectory(Path.Combine(_dir, "profiles", ProfileStore.FileNameFor("p2")) + ".tmp");
        var vm = new ManageTagsViewModel(new TagMaintenance(_profiles, _tags));
        var window = new ManageTagsWindow(vm);
        window.Show();
        window.UpdateLayout();

        vm.SelectedRow = vm.Rows.Single(r => r.Name == "DEV");
        vm.NameText = "TEST";
        vm.RenameCommand.Execute(null);
        window.UpdateLayout();
        Assert.NotNull(vm.StatusMessage);

        vm.SelectedRow = vm.Rows.Single(r => r.Name == "DEV");
        window.UpdateLayout();
        vm.NameText = "TEST";
        vm.RenameCommand.Execute(null);
        window.UpdateLayout();
        Assert.NotNull(vm.PendingConfirmation);

        var scroll = window.FindControl<ScrollViewer>("PanelScroll")!;
        var strip = window.FindControl<Border>("ConfirmationStrip")!;
        var scrollBottom = scroll.TranslatePoint(new Point(0, scroll.Bounds.Height), window)!.Value.Y;
        var stripTop = strip.TranslatePoint(new Point(0, 0), window)!.Value.Y;
        Assert.True(scrollBottom <= stripTop + 0.5,
            $"the panel's scroll region ends at {scrollBottom} but the confirmation strip starts at {stripTop}");
    }

    [AvaloniaFact]
    public void Done_closes_the_window()
    {
        var (window, _) = Show();

        window.FindControl<Button>("DoneButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.False(window.IsVisible);
    }

    private static Point Centre(Window window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static Button Swatch(ManageTagsWindow window, string colour) =>
        Descendants<Button>(window.FindControl<ItemsControl>("SwatchList")!).Single(b => ToolTip.GetTip(b) as string == colour);

    /// <summary>A real press and release at the swatch's centre, not a raised Click: the 2026-09-13 in-app pass
    /// saw a DevTools pointer click on a swatch answer handled and change nothing, so the pointer path is what
    /// this guards.</summary>
    [AvaloniaFact]
    public void A_real_pointer_click_on_a_swatch_recolours_the_tag()
    {
        var (window, vm) = Show();
        Select(window, vm, "TLS");
        var centre = Centre(window, Swatch(window, "Teal"));

        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);

        Assert.Equal(TagColor.Teal, _tags.Load().ColorOf("TLS"));
    }

    /// <summary>Plan D6: Fluent swaps the content presenter's background for a theme brush on :pointerover and
    /// :pressed. The window style that puts the swatch's own brush back must actually resolve — the in-app pass
    /// found it bound against the presenter's null DataContext, so a hovered swatch drew as a hole.</summary>
    [AvaloniaFact]
    public void A_swatch_keeps_its_colour_under_the_pointer_and_while_pressed()
    {
        var (window, vm) = Show();
        Select(window, vm, "PROD");
        var swatch = Swatch(window, "Green");
        var expected = ((SwatchOption)swatch.DataContext!).Brush;
        var presenter = Descendants<Avalonia.Controls.Presenters.ContentPresenter>(swatch).Single();
        var centre = Centre(window, swatch);

        window.MouseMove(centre);
        Assert.Contains(":pointerover", swatch.Classes);
        Assert.Same(expected, presenter.Background);

        window.MouseDown(centre, MouseButton.Left);
        Assert.Contains(":pressed", swatch.Classes);
        Assert.Same(expected, presenter.Background);
        window.MouseUp(centre, MouseButton.Left);
    }
}
