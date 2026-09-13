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

        var box = window.FindControl<ComboBox>("QuickConnectBox")!;
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

        var box = window.FindControl<ComboBox>("QuickConnectBox")!;
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

    private static Rect InWindow(Window window, Control control) =>
        new(control.TranslatePoint(new Point(0, 0), window)!.Value, control.Bounds.Size);

    /// <summary>Quick Connect and the filter are both a text box beside a control, and stacked a few pixels apart
    /// they read as one form: a profile name typed into the wrong one connects nowhere or filters to nothing. So
    /// Quick Connect is a footer, below the list, and the list's own search stays above it.</summary>
    [AvaloniaFact]
    public void Quick_connect_is_a_footer_below_the_list()
    {
        var store = new ProfileStore(_dir);
        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { });
        window.Show();
        window.UpdateLayout();

        var list = InWindow(window, Descendants<ListBox>(window).Single());
        var filter = InWindow(window, window.FindControl<TextBox>("FilterBox")!);
        var quick = InWindow(window, window.FindControl<ComboBox>("QuickConnectBox")!);

        Assert.True(filter.Bottom <= list.Top + 0.5, $"the filter ends at {filter.Bottom} and the list starts at {list.Top}");
        Assert.True(quick.Top >= list.Bottom, $"Quick Connect starts at {quick.Top} and the list ends at {list.Bottom}");
    }

    /// <summary>The filter row belongs to the list, so it ends where the list ends rather than running over the
    /// button column — at the minimum width too, where a fixed-width drop-down would be the first thing to spill.</summary>
    [AvaloniaFact]
    public void The_filter_row_is_exactly_as_wide_as_the_list()
    {
        var store = new ProfileStore(_dir);
        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { });
        window.Width = window.MinWidth;
        window.Show();
        window.UpdateLayout();

        var list = InWindow(window, Descendants<ListBox>(window).Single());
        var filter = InWindow(window, window.FindControl<TextBox>("FilterBox")!);
        var scope = InWindow(window, window.FindControl<ComboBox>("ScopeBox")!);

        Assert.Equal(list.Left, filter.Left, 0.5);
        Assert.Equal(list.Right, scope.Right, 0.5);
    }

    /// <summary>Tab from a picker with nothing focused lands in the filter, not Quick Connect: the list is what most
    /// openings are for.</summary>
    [AvaloniaFact]
    public void The_first_tab_lands_in_the_filter()
    {
        var store = new ProfileStore(_dir);
        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { });
        window.Show();
        window.UpdateLayout();

        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);

        Assert.Same(window.FindControl<TextBox>("FilterBox"), window.FocusManager?.GetFocusedElement());
    }

    private ProfilePickerWindow PickerWithRecent(params string[] entries)
    {
        var recent = new RecentHostsStore(Path.Combine(_dir, "config", "recent-hosts.json"));
        recent.Save(RecentHosts.From(entries));
        var window = new ProfilePickerWindow(new ProfileStore(_dir), (_, _) => { }, () => { }, recentHosts: recent);
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static ComboBox QuickConnect(Window window) => window.FindControl<ComboBox>("QuickConnectBox")!;

    /// <summary>Opens the drop-down and lets the popup position itself. Without the jobs and a render tick the
    /// overlay popup is still at the window's origin, and a pointer aimed at an item hits nothing.</summary>
    private static void OpenList(Window window, ComboBox box)
    {
        box.IsDropDownOpen = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();
    }

    private static ComboBoxItem RecentItem(ComboBox box, string entry) =>
        box.GetRealizedContainers().OfType<ComboBoxItem>().Single(i => (i.DataContext as string) == entry);

    [AvaloniaFact]
    public void Picking_a_recent_host_puts_it_in_the_box()
    {
        var window = PickerWithRecent("a.example", "L:b.example:992");
        var vm = (ProfilePickerViewModel)window.DataContext!;

        QuickConnect(window).SelectedItem = "L:b.example:992";

        Assert.Equal("L:b.example:992", vm.QuickConnectText);
    }

    /// <summary>A real press and release on the ×, because the risk is the pointer: the press landing on the row
    /// as well would pick the entry into the box and close the drop-down.</summary>
    [AvaloniaFact]
    public void The_cross_forgets_that_entry_without_picking_it()
    {
        var window = PickerWithRecent("a.example", "b.example");
        var vm = (ProfilePickerViewModel)window.DataContext!;
        var box = QuickConnect(window);
        vm.QuickConnectText = "typed.example";
        OpenList(window, box);

        var cross = RecentItem(box, "a.example").GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ForgetButton");
        var host = TopLevel.GetTopLevel(cross)!;
        var centre = cross.TranslatePoint(new Point(cross.Bounds.Width / 2, cross.Bounds.Height / 2), host)!.Value;
        host.MouseDown(centre, MouseButton.Left);
        host.MouseUp(centre, MouseButton.Left);

        Assert.Equal(["b.example"], vm.RecentEntries);
        Assert.Equal("typed.example", vm.QuickConnectText);
        Assert.True(box.IsDropDownOpen);
    }

    [AvaloniaFact]
    public void Delete_forgets_the_highlighted_entry()
    {
        var window = PickerWithRecent("a.example", "b.example");
        var vm = (ProfilePickerViewModel)window.DataContext!;
        var box = QuickConnect(window);
        box.Focus();
        OpenList(window, box);

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        var highlighted = (string)box.SelectedItem!;
        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);

        Assert.Equal(new[] { "a.example", "b.example" }.Where(e => e != highlighted), vm.RecentEntries);
    }

    /// <summary>Enter connects what is in the box whether or not the list is open, and highlighting an entry is
    /// what put it there, so one Enter recalls and connects — the address bar's behaviour. The editable ComboBox
    /// never closes its own list on Enter, so the handler does.</summary>
    [AvaloniaFact]
    public void Enter_with_the_list_open_connects_the_highlighted_entry_and_closes_the_list()
    {
        var recent = new RecentHostsStore(Path.Combine(_dir, "config", "recent-hosts.json"));
        recent.Save(RecentHosts.From(["a.example", "b.example"]));
        SessionProfile? opened = null;
        var window = new ProfilePickerWindow(new ProfileStore(_dir), (p, _) => opened = p, () => { }, recentHosts: recent);
        window.Show();
        window.UpdateLayout();
        var box = QuickConnect(window);
        box.Focus();
        OpenList(window, box);

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        var highlighted = (string)box.SelectedItem!;
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal(highlighted, opened?.Host);
        Assert.False(box.IsDropDownOpen);
    }

    [AvaloniaFact]
    public void Forgetting_the_last_entry_closes_the_list()
    {
        var window = PickerWithRecent("a.example");
        var vm = (ProfilePickerViewModel)window.DataContext!;
        var box = QuickConnect(window);
        box.IsDropDownOpen = true;

        vm.RemoveRecentHostCommand.Execute("a.example");

        Assert.False(box.IsDropDownOpen);
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

    private static ListBoxItem Row(Window window, string name) =>
        Descendants<ListBoxItem>(window).Single(i => ((ProfileRow)i.DataContext!).Name == name);

    private static Point Centre(Window window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    /// <summary>A real right button press and release at the named row's centre, returning the menu that opened.
    /// Found by being open rather than through the clicked container: Show() posts the window's activation and
    /// the first headless input flushes it, so a Reload runs inside that click — one that now keeps the containers
    /// when the store is unchanged, but the lookup does not lean on that. A headless press never activates a
    /// window itself, so the reload-versus-press ordering of a real platform is not something this can see.</summary>
    private static ContextMenu RightClickRow(Window window, string name)
    {
        var centre = Centre(window, Row(window, name));
        window.MouseDown(centre, MouseButton.Right);
        window.MouseUp(centre, MouseButton.Right);
        return OpenMenu(window);
    }

    private static ContextMenu OpenMenu(Window window) =>
        Descendants<ListBoxItem>(window).Select(i => i.ContextMenu).OfType<ContextMenu>().Single(m => m.IsOpen);

    private static MenuItem Entry(ContextMenu menu, string header) =>
        menu.Items.OfType<MenuItem>().Single(m => (m.Header as string) == header);

    [AvaloniaFact]
    public void Right_clicking_a_row_selects_it_and_opens_its_menu()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "alpha", Host = "h" });
        store.Save(new SessionProfile { Name = "zeta", Host = "h" });
        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { });
        window.Show();
        window.UpdateLayout();
        var vm = (ProfilePickerViewModel)window.DataContext!;
        vm.SelectedRow = vm.VisibleRows.Single(r => r.Name == "alpha");

        var menu = RightClickRow(window, "zeta");

        Assert.Equal("zeta", vm.SelectedRow?.Name);
        Assert.Equal("zeta", (menu.DataContext as ProfileRow)?.Name);
        Assert.Equal(["Connect", "Edit...", "Mark as FAVORITE"], menu.Items.OfType<MenuItem>().Select(m => m.Header as string));
    }

    [AvaloniaFact]
    public void The_row_menu_stars_the_row_it_was_opened_on()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "alpha", Host = "h" });
        store.Save(new SessionProfile { Name = "zeta", Host = "h" });
        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { });
        window.Show();
        window.UpdateLayout();

        Entry(RightClickRow(window, "zeta"), "Mark as FAVORITE").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        window.UpdateLayout();

        Assert.True(store.Load("zeta")!.Tags.Contains("FAVORITE"));
        Assert.False(store.Load("alpha")!.Tags.Contains("FAVORITE"));
        Assert.Contains(Descendants<TextBlock>(Row(window, "zeta")), t => t.Name == "StarGlyph" && t.IsVisible);
    }

    [AvaloniaFact]
    public void The_row_menu_connects_the_row_it_was_opened_on()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "alpha", Host = "h" });
        store.Save(new SessionProfile { Name = "zeta", Host = "h" });
        SessionProfile? opened = null;
        var window = new ProfilePickerWindow(store, (p, _) => opened = p, () => { });
        window.Show();
        window.UpdateLayout();

        Entry(RightClickRow(window, "zeta"), "Connect").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal("zeta", opened?.Name);
    }

    [AvaloniaFact]
    public void The_row_menu_edits_the_row_it_was_opened_on()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "alpha", Host = "h" });
        store.Save(new SessionProfile { Name = "zeta", Host = "h" });
        SessionProfile? opened = null;
        var window = new ProfilePickerWindow(store, (p, _) => opened = p, () => { });
        window.Show();
        window.UpdateLayout();

        Entry(RightClickRow(window, "zeta"), "Edit...").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        var editor = Assert.IsType<ProfileEditorWindow>(Assert.Single(window.OwnedWindows));
        Assert.Equal("zeta", ((ProfileEditorViewModel)editor.DataContext!).Name);
        Assert.Null(opened);
        editor.Close();
    }

    [AvaloniaFact]
    public void A_full_profiles_menu_offers_favorite_disabled_and_says_why()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "full", Host = "h", Tags = TagSet.From(Enumerable.Range(0, TagSet.MaxTags).Select(i => $"T{i}")) });
        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { });
        window.Show();
        window.UpdateLayout();

        var entry = Entry(RightClickRow(window, "full"), $"Mark as FAVORITE (already {TagSet.MaxTags} tags)");
        // Effectively: a command-driven item greys through CanExecute, not through its own IsEnabled.
        Assert.False(entry.IsEffectivelyEnabled);

        entry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.False(store.Load("full")!.Tags.Contains("FAVORITE"));
    }

    /// <summary>Opened without a pointer, as the context-menu key does, on a row that is NOT the selection: an entry
    /// must act on the row the menu belongs to. A right click selects its row on the press, so the pointer tests
    /// above cannot tell "the menu's row" from "the selection" apart.</summary>
    [AvaloniaFact]
    public void A_menu_opened_from_the_keyboard_connects_its_own_row_not_the_selection()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "alpha", Host = "h" });
        store.Save(new SessionProfile { Name = "zeta", Host = "h" });
        SessionProfile? opened = null;
        var window = new ProfilePickerWindow(store, (p, _) => opened = p, () => { });
        window.Show();
        window.UpdateLayout();
        var vm = (ProfilePickerViewModel)window.DataContext!;
        vm.SelectedRow = vm.VisibleRows.Single(r => r.Name == "alpha");

        Row(window, "zeta").RaiseEvent(new ContextRequestedEventArgs());
        var menu = OpenMenu(window);
        Assert.Equal("zeta", (menu.DataContext as ProfileRow)?.Name);
        Assert.Equal("alpha", vm.SelectedRow?.Name);

        Entry(menu, "Connect").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal("zeta", opened?.Name);
    }

    [AvaloniaFact]
    public void A_menu_opened_from_the_keyboard_stars_its_own_row_not_the_selection()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "alpha", Host = "h" });
        store.Save(new SessionProfile { Name = "zeta", Host = "h" });
        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { });
        window.Show();
        window.UpdateLayout();
        var vm = (ProfilePickerViewModel)window.DataContext!;
        vm.SelectedRow = vm.VisibleRows.Single(r => r.Name == "alpha");

        Row(window, "zeta").RaiseEvent(new ContextRequestedEventArgs());
        Entry(OpenMenu(window), "Mark as FAVORITE").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.True(store.Load("zeta")!.Tags.Contains("FAVORITE"));
        Assert.False(store.Load("alpha")!.Tags.Contains("FAVORITE"));
    }

    /// <summary>Control-click is how a one-button Mac right-clicks, and the macOS backend delivers it as a left
    /// press carrying the Control modifier; the window turns that into the ContextRequested a right button raises.
    /// Elsewhere Control toggles the selection and the row menu stays on the right button, so this is macOS-only
    /// in production and here.</summary>
    [AvaloniaFact]
    public void Control_click_opens_the_row_menu_on_macOS()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Control-click is the macOS gesture");
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "alpha", Host = "h" });
        store.Save(new SessionProfile { Name = "zeta", Host = "h" });
        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { });
        window.Show();
        window.UpdateLayout();

        var centre = Centre(window, Row(window, "zeta"));
        window.MouseDown(centre, MouseButton.Left, RawInputModifiers.Control);
        window.MouseUp(centre, MouseButton.Left, RawInputModifiers.Control);

        Assert.Equal("zeta", (OpenMenu(window).DataContext as ProfileRow)?.Name);
    }

    [AvaloniaFact]
    public void Tags_opens_manage_tags_over_the_picker()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "a", Host = "h", Tags = TagSet.From(["PROD"]) });
        var window = new ProfilePickerWindow(store, (_, _) => { }, () => { }, new TagRegistryStore(Path.Combine(_dir, "tags.json")));
        window.Show();
        var vm = (ProfilePickerViewModel)window.DataContext!;

        var button = window.FindControl<Button>("TagsButton")!;
        Assert.Same(vm.ManageTagsCommand, button.Command);
        Assert.True(button.IsEffectivelyEnabled);

        // Through the command, because raising Button.ClickEvent runs Click handlers but never a bound Command.
        vm.ManageTagsCommand.Execute(null);

        var dialog = Assert.IsType<ManageTagsWindow>(Assert.Single(window.OwnedWindows));
        Assert.Contains(((ManageTagsViewModel)dialog.DataContext!).Rows, r => r.Name == "PROD");
        dialog.Close();
    }
}
