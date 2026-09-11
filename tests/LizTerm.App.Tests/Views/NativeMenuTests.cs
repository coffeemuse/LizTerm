// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using LizTerm.App.Controls;
using LizTerm.App.Menus;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Views;

/// <summary>The native menu definition, reached through NativeMenu.GetMenu rather than the visual tree:
/// NativeMenuItem is not a Control, so FindControl cannot see it. Bindings on it do resolve under the
/// headless platform, two-way write-back included, which is what makes these assertions possible.</summary>
public class NativeMenuTests
{
    /// <summary>What the application menu *declares*, which on macOS is not what a user sees. Avalonia's
    /// AvaloniaNativeMenuExporter.SetMenu appends AppKit's standard block — Services, Hide, Hide Others, Show All
    /// and Quit — to this very NativeMenu instance unless MacOSPlatformOptions.DisableDefaultApplicationMenuItems
    /// is set, which Program.cs does not set. The standard block is therefore Avalonia's to supply and is
    /// invisible to this test: the native exporter never runs under headless, so nothing here can observe it.
    ///
    /// Which is why there is no Quit of our own. Measured on macOS 15 / Avalonia 12.1.2, declaring one shipped
    /// two Quit items with the same Cmd+Q and opposite behaviour: ours called App.Quit() → Shutdown() (forced),
    /// Avalonia's calls TryShutdown(0), which a running IND$FILE transfer correctly refuses. The non-forcing one
    /// is the semantic this app wants.</summary>
    [AvaloniaFact]
    public void The_application_menu_declares_about_and_preferences_and_no_quit_of_its_own()
    {
        var menu = NativeMenu.GetMenu(Application.Current!);

        Assert.NotNull(menu);
        var headers = menu!.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).Select(i => i.Header!).ToArray();
        Assert.Equal(["About LizTerm", "Preferences..."], headers);
    }

    /// <summary>The one gesture outside Edit, deliberately (settings spec §5.4): Cmd-comma is where every macOS
    /// user looks for Preferences, the application menu exists only on macOS, and DefaultKeymap binds no Cmd
    /// chord, so nothing is taken from the host. About stays bare, and so does everything AppKit appends.</summary>
    [AvaloniaFact]
    public void The_application_menu_carries_cmd_comma_on_preferences_and_nothing_else()
    {
        var menu = NativeMenu.GetMenu(Application.Current!)!;
        var about = MenuLookup.Item(menu, "About LizTerm")!;
        var preferences = MenuLookup.Item(menu, "Preferences...")!;

        Assert.Null(about.Gesture);
        Assert.Equal(new KeyGesture(Key.OemComma, KeyModifiers.Meta), preferences.Gesture);
        Assert.True(preferences.HasClickHandlers);
        Assert.True(about.HasClickHandlers);
    }

    private static (SessionWindow Window, SessionViewModel Vm, FakeEmulatorSession Session, FakeTextClipboard Clipboard) Show(bool useNativeMenu = true)
    {
        var session = new FakeEmulatorSession();
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(2, 3, "hello", null, null, null);
        session.CurrentScreen = buffer.Snapshot();
        var clipboard = new FakeTextClipboard();
        var vm = new SessionViewModel(session, action => action(), clipboard);
        var window = new SessionWindow(useNativeMenu) { DataContext = vm };
        window.Show();
        return (window, vm, session, clipboard);
    }

    private static NativeMenuItem Item(SessionWindow window, string top, string child) =>
        MenuLookup.Item(NativeMenu.GetMenu(window), top, child)
        ?? throw new InvalidOperationException($"no native menu item {top} > {child}");

    /// <summary>The crosshair modes live one level deeper than everything else, in a View &gt; Crosshair
    /// submenu: "Horizontal" and "Vertical" sitting bare under View read as window tiling.</summary>
    private static NativeMenuItem CrosshairItem(SessionWindow window, string header) =>
        MenuLookup.Item(Item(window, "_View", "_Crosshair").Menu, header)
        ?? throw new InvalidOperationException($"no native menu item _View > _Crosshair > {header}");

    [AvaloniaFact]
    public void The_window_menu_has_the_same_five_top_level_menus_as_the_classic_one()
    {
        var (window, _, _, _) = Show();

        var headers = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Select(i => i.Header!).ToArray();
        Assert.Equal(["_File", "_Edit", "_View", "_Keys", "_Help"], headers);
    }

    /// <summary>A NativeMenuItem never toggles itself — RaiseClicked raises Click and executes Command and
    /// never touches IsChecked — so the handler sets the view model and the OneWay bindings carry every check
    /// mark, the three corrections to false included. Driven through RaiseClicked because that is the one entry
    /// point both real renderers use; assigning IsChecked instead would only prove a binding round-trips.
    ///
    /// All four modes are exercised, not just Vertical: CrosshairModeConverter.Convert throws on a parameter
    /// that does not parse as a CrosshairMode, but Avalonia's binding engine swallows a converter's exception
    /// rather than propagating it, so a mistyped ConverterParameter in SessionWindow.axaml (say "Horizantal" for
    /// the Horizontal item) would surface here only as a wrong IsChecked value on that one item's own click —
    /// never a thrown exception — the failure mode a test that only ever clicks Vertical could never see.</summary>
    [AvaloniaFact]
    public void Choosing_a_crosshair_mode_checks_exactly_that_item()
    {
        var (window, vm, _, _) = Show();
        var modes = new[] { CrosshairMode.None, CrosshairMode.Horizontal, CrosshairMode.Vertical, CrosshairMode.Both };
        var items = new[] { "_None", "_Horizontal", "_Vertical", "_Both" }
            .Select(header => CrosshairItem(window, header)).ToArray();

        for (var chosen = 0; chosen < modes.Length; chosen++)
        {
            ((INativeMenuItemExporterEventsImplBridge)items[chosen]).RaiseClicked();

            Assert.Equal(modes[chosen], vm.Settings.Crosshair);
            Assert.Equal(
                Enumerable.Range(0, modes.Length).Select(i => i == chosen),
                items.Select(i => i.IsChecked));
        }
    }

    [AvaloniaFact]
    public void The_crosshair_reaches_the_terminal_screen()
    {
        var (window, vm, _, _) = Show();

        vm.Settings.Crosshair = CrosshairMode.Both;

        Assert.Equal(CrosshairMode.Both, window.FindControl<TerminalScreen>("Screen")!.Crosshair);
    }

    [AvaloniaFact]
    public void The_blink_setting_reaches_the_terminal_screen()
    {
        var (window, vm, _, _) = Show();
        var screen = window.FindControl<TerminalScreen>("Screen")!;
        Assert.True(screen.BlinkEnabled);

        vm.Settings.Blink = false;

        Assert.False(screen.BlinkEnabled);
    }

    /// <summary>The four modes are grouped under a Crosshair submenu rather than sitting bare under View,
    /// where "Horizontal" and "Vertical" read as window tiling to anyone who has not been told otherwise —
    /// which is exactly what a first look at this menu produced. Asserted on both menus, because the parity
    /// walk compares structure but this is the structure it would be comparing.</summary>
    [AvaloniaFact]
    public void The_crosshair_modes_are_grouped_under_their_own_submenu()
    {
        var (window, _, _, _) = Show();
        var expected = new[] { "_None", "_Horizontal", "_Vertical", "_Both" };

        var nativeView = MenuLookup.Item(NativeMenu.GetMenu(window), "_View")!;
        var nativeChildren = nativeView.Menu!.Items.OfType<NativeMenuItem>().ToArray();
        var nativeCrosshair = Assert.Single(nativeChildren);
        Assert.Equal("_Crosshair", nativeCrosshair.Header);
        Assert.Equal(expected, nativeCrosshair.Menu!.Items.OfType<NativeMenuItem>().Select(i => i.Header));

        var classicView = window.FindControl<Menu>("ClassicMenu")!.Items.OfType<MenuItem>()
            .Single(i => (string)i.Header! == "_View");
        var classicCrosshair = Assert.Single(classicView.Items.OfType<MenuItem>());
        Assert.Equal("_Crosshair", classicCrosshair.Header);
        Assert.Equal(expected, classicCrosshair.Items.OfType<MenuItem>().Select(i => (string)i.Header!));
    }

    [AvaloniaFact]
    public void File_transfer_follows_the_connection_state()
    {
        var (window, _, session, _) = Show();
        var item = Item(window, "_File", "IND$FILE _Transfer...");

        Assert.False(item.IsEnabled);
        session.RaiseConnection(ConnectionState.Connected3270);
        Assert.True(item.IsEnabled);
        session.RaiseConnection(ConnectionState.Disconnected);
        Assert.False(item.IsEnabled);
    }

    /// <summary>The delicate one, driven through RaiseClicked because that is the single entry point both real
    /// renderers use — Avalonia's macOS exporter calls it from the NSMenuItem's action, and the in-window
    /// NativeMenuBar fallback calls it from its presenter's Click. Assigning IsChecked from the test instead
    /// would prove only that a binding round-trips: nothing in the app ever writes that property, and
    /// RaiseClicked itself never touches it, which is why this item needs a Click handler and a OneWay binding
    /// where the classic one needs neither.</summary>
    [AvaloniaFact]
    public void Clicking_the_wire_log_item_toggles_the_log_and_the_check_mark()
    {
        var (window, vm, _, _) = Show();
        var item = Item(window, "_Help", "_Wire Log");
        var click = (INativeMenuItemExporterEventsImplBridge)item;

        Assert.Equal(MenuItemToggleType.CheckBox, item.ToggleType);
        Assert.False(item.IsChecked);

        var directory = Path.Combine(Path.GetTempPath(), "lizterm-native-" + Guid.NewGuid().ToString("N"));
        vm.WireLogDirectory = directory;
        try
        {
            click.RaiseClicked();
            Assert.True(vm.IsWireLogging);
            Assert.True(item.IsChecked);

            click.RaiseClicked();
            Assert.False(vm.IsWireLogging);
            Assert.False(item.IsChecked);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The check mark still has to follow the view model, because that is how the correction to false
    /// reaches the menu when a log will not open — SessionViewModel marshals it through dispatch, and a value
    /// corrected from inside its own change notification is invisible to a two-way binding mid-write.</summary>
    [AvaloniaFact]
    public void The_wire_log_check_mark_follows_the_view_model()
    {
        var (window, vm, _, _) = Show();
        var item = Item(window, "_Help", "_Wire Log");

        var directory = Path.Combine(Path.GetTempPath(), "lizterm-native-" + Guid.NewGuid().ToString("N"));
        vm.WireLogDirectory = directory;
        try
        {
            vm.IsWireLogging = true;
            Assert.True(item.IsChecked);

            vm.IsWireLogging = false;
            Assert.False(item.IsChecked);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The in-window NativeMenuBar fallback reaches the same item by a different route, and the two
    /// must not both act. DefaultMenuInteractionHandler.Click toggles its MenuItem's IsChecked and only then
    /// raises Click, and the presenter binds that MenuItem's IsChecked two-way to this NativeMenuItem's — so the
    /// fallback writes IsChecked first and calls RaiseClicked second. This replays that order: with a TwoWay
    /// binding to the view model the write would start the log and the click would stop it again, leaving the
    /// user's click with nothing to show for it. OneWay is what makes the handler the only thing that acts.</summary>
    [AvaloniaFact]
    public void The_in_window_fallback_order_toggles_the_log_exactly_once()
    {
        var (window, vm, _, _) = Show();
        var item = Item(window, "_Help", "_Wire Log");

        var directory = Path.Combine(Path.GetTempPath(), "lizterm-native-" + Guid.NewGuid().ToString("N"));
        vm.WireLogDirectory = directory;
        try
        {
            item.IsChecked = true;
            ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();

            Assert.True(vm.IsWireLogging);
            Assert.True(item.IsChecked);

            // And the write-back has not cost the item its binding: a local value set at the same priority is
            // overridden again by the binding's next production, so the check mark still follows the view model.
            vm.IsWireLogging = false;
            Assert.False(item.IsChecked);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>What the parity guard cannot see, and what shipped the wire log item dead on macOS: Avalonia's
    /// exporter validates every NSMenuItem with <c>(Command != null || HasClickHandlers) &amp;&amp; IsEnabled</c>
    /// (__MicroComIAvnMenuItemProxy.UpdateAction), and the in-window fallback gates RaiseClicked on
    /// HasClickHandlers alone. An item carrying only a binding satisfies neither, so it is greyed out on macOS
    /// and inert everywhere — while a classic MenuItem with the same null Command works fine, which is why
    /// comparing the two menus' Commands passes straight over it.</summary>
    [AvaloniaFact]
    public void Every_native_item_can_actually_be_activated()
    {
        var (window, _, _, _) = Show();

        foreach (var top in NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>())
            AssertEveryLeafIsActivatable(top.Header!, top.Menu!);
    }

    /// <summary>Recurses, because View &gt; Crosshair put items a level deeper than this guard used to look and
    /// an unwalked submenu is an unguarded one. A submenu *parent* is exempt: it opens its submenu rather than
    /// activating, and the exporter does not grey it out for carrying no Command — only its leaves must answer.
    /// NativeMenuItemSeparator derives from NativeMenuItem, so OfType alone would demand a handler on the
    /// dividers too.</summary>
    private static void AssertEveryLeafIsActivatable(string path, NativeMenu menu)
    {
        foreach (var item in menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator))
        {
            if (item.Menu is { } submenu)
            {
                AssertEveryLeafIsActivatable($"{path} > {item.Header}", submenu);
                continue;
            }

            Assert.True(item.Command is not null || item.HasClickHandlers,
                $"{path} > {item.Header}: no Command and no Click handler, so macOS greys it out");
        }
    }

    /// <summary>On a CI machine this covers the visible-in-Help branch only; the macOS branch is covered by
    /// running the app on the Mac, which the spec's section 8 requires anyway. No mutable platform static is
    /// introduced to close that gap — it would make every menu test order-dependent.</summary>
    [AvaloniaFact]
    public void About_is_in_the_help_menu_on_this_platform_exactly_when_the_strategy_says_so()
    {
        var (window, _, _, _) = Show();
        var expected = MenuStrategy.AboutInHelpMenu(OperatingSystem.IsMacOS());

        Assert.Equal(expected, Item(window, "_Help", "_About LizTerm...").IsVisible);
        Assert.Equal(expected, window.FindControl<MenuItem>("AboutMenuItem")!.IsVisible);
    }

    /// <summary>And the divider above it goes too. Nothing collapses a trailing separator — not NSMenu, not the
    /// classic Menu — so hiding About on its own leaves the macOS Help menu ending in a line with nothing under
    /// it. The exporter honours IsVisible on a separator because NativeMenuItemSeparator derives from
    /// NativeMenuItem.</summary>
    [AvaloniaFact]
    public void The_separator_above_about_is_hidden_with_it()
    {
        var (window, _, _, _) = Show();
        var expected = MenuStrategy.AboutInHelpMenu(OperatingSystem.IsMacOS());

        var separator = MenuLookup.SeparatorAbove(Item(window, "_Help", "_About LizTerm..."));
        Assert.NotNull(separator);
        Assert.Equal(expected, separator!.IsVisible);
        Assert.Equal(expected, window.FindControl<Separator>("AboutSeparator")!.IsVisible);
    }

    /// <summary>The native Edit items are driven by Click handlers, so they get none of the greying a command's
    /// CanExecute gives the classic ones — and on macOS they are also key equivalents, so an enabled item is an
    /// offer the app cannot honour. They bind the same predicates instead.</summary>
    [AvaloniaFact]
    public void The_edit_items_follow_the_same_predicates_as_the_classic_commands()
    {
        var (window, vm, session, _) = Show();
        var copy = Item(window, "_Edit", "_Copy");
        var paste = Item(window, "_Edit", "_Paste");
        var selectAll = Item(window, "_Edit", "Select _All");

        // A screen but no selection: copying has nothing to copy, selecting all has something to select.
        Assert.False(copy.IsEnabled);
        Assert.True(selectAll.IsEnabled);
        Assert.False(paste.IsEnabled);

        vm.Selection = ScreenRegion.FromCorners(2, 3, 2, 7);
        Assert.True(copy.IsEnabled);

        session.RaiseConnection(ConnectionState.Connected3270);
        Assert.True(paste.IsEnabled);
        session.RaiseConnection(ConnectionState.Disconnected);
        Assert.False(paste.IsEnabled);
    }

    /// <summary>MenuLookup.Required is what stops a renamed header from being swallowed. Null means one thing
    /// only — the classic strategy emptied the menu, and the window asks through ExportedMenu, which answers
    /// null for it — and a menu that is there without the item is a typo the
    /// code-behind would otherwise answer by silently skipping, leaving About duplicated on macOS or the Edit
    /// key equivalents quietly gone. Renaming a header in both menus at once keeps the parity guard green, so
    /// nothing else would notice.</summary>
    [AvaloniaFact]
    public void A_lookup_that_misses_on_an_attached_menu_throws_rather_than_no_opping()
    {
        var (window, _, _, _) = Show();
        var menu = NativeMenu.GetMenu(window);

        Assert.Null(MenuLookup.Required(null, "_Help", "_About LizTerm..."));
        Assert.NotNull(MenuLookup.Required(menu, "_Help", "_About LizTerm..."));

        var ex = Assert.Throws<InvalidOperationException>(() => MenuLookup.Required(menu, "_Help", "_About LizTerm"));
        Assert.Contains("_About LizTerm", ex.Message);
        Assert.Throws<InvalidOperationException>(() => MenuLookup.Required(menu, "_Halp", "_Wire Log"));
    }

    /// <summary>Measured: with a NativeMenu installed and the classic Menu still visible, macOS drew both — an
    /// in-window bar beneath a system bar. Hiding the classic one under the native strategy is required, not tidy.</summary>
    [AvaloniaFact]
    public void Exactly_one_renderer_is_visible_under_each_strategy()
    {
        var (native, _, _, _) = Show(useNativeMenu: true);
        Assert.False(native.FindControl<Menu>("ClassicMenu")!.IsVisible);
        Assert.True(native.FindControl<NativeMenuBar>("NativeBar")!.IsVisible);

        var (classic, _, _, _) = Show(useNativeMenu: false);
        Assert.True(classic.FindControl<Menu>("ClassicMenu")!.IsVisible);
        Assert.False(classic.FindControl<NativeMenuBar>("NativeBar")!.IsVisible);
    }

    /// <summary>Hiding the renderers is not what turns the native path off, so this asserts the thing that does.
    /// A window's NativeMenu is exported through the window's own ITopLevelNativeMenuExporter — NativeMenu's
    /// MenuProperty change handler calls SetNativeMenu on it — with no NativeMenuBar involved at all;
    /// NativeMenuBar merely binds the same property to draw an in-window fallback. So a hidden NativeMenuBar
    /// still leaves AppKit key equivalents installed and a system menu bar drawn beside the classic one on
    /// macOS, and still hands the menu to a Linux global-menu registrar (Plasma's Application Menu applet,
    /// Unity) while the classic bar draws it in-window. LIZTERM_MENU=classic is the escape hatch for exactly the
    /// case where a native gesture is swallowing a 3270 key, so it has to empty the definition, not just hide a
    /// control.
    ///
    /// Emptied, not replaced (#60). Avalonia 12.1.2's macOS exporter initialises its native proxy with the
    /// first NativeMenu instance the window is given and its Update throws "The menu being updated does not
    /// match" for any other instance — and SetNativeMenu(null) normalises null to a *fresh* NativeMenu, so the
    /// old detach (SetMenu(this, null)) threw from the window's constructor on every macOS launch with
    /// LIZTERM_MENU=classic. The one instance the exporter will accept is the declared one, so the classic
    /// strategy keeps it attached and removes its items: Update on the same instance with no items removes and
    /// disposes every native item, leaving no key equivalent installed. The attached property is the assertion
    /// because it is the input to every exporter; headless offers no ITopLevelNativeMenuExporter, so
    /// NativeMenu.GetIsNativeMenuExported is false either way here and would prove nothing — and nothing
    /// headless can tell this apart from SetMenu(this, new NativeMenu()), which passes here and throws on
    /// macOS. The launch with LIZTERM_MENU=classic on a Mac is the other half of this guard.</summary>
    [AvaloniaFact]
    public void The_classic_strategy_empties_the_window_native_menu_without_replacing_it()
    {
        var (native, _, _, _) = Show(useNativeMenu: true);
        Assert.NotEmpty(NativeMenu.GetMenu(native)!.Items);

        var (classic, _, _, _) = Show(useNativeMenu: false);
        var menu = NativeMenu.GetMenu(classic);
        Assert.NotNull(menu);
        Assert.Empty(menu.Items);
    }

    /// <summary>The detach must not cost the classic menu its shortcut hints: ShowPlatformGestures sets the
    /// three classic InputGestures and then looks the three native Edit items up, and under this strategy the
    /// declared menu is still attached but empty. Every native lookup finding nothing has to be a no-op, not a
    /// throw — MenuLookup.Required throws for a menu that is there and lacks the item, so the window has to
    /// know not to ask.</summary>
    [AvaloniaFact]
    public void The_classic_menu_keeps_its_gestures_when_the_native_menu_is_emptied()
    {
        var (window, _, _, _) = Show(useNativeMenu: false);
        var hotkeys = window.GetPlatformSettings()!.HotkeyConfiguration;

        Assert.Equal(hotkeys.Copy.FirstOrDefault(), window.FindControl<MenuItem>("CopyMenuItem")!.InputGesture);
        Assert.Equal(hotkeys.Paste.FirstOrDefault(), window.FindControl<MenuItem>("PasteMenuItem")!.InputGesture);
        Assert.Equal(hotkeys.SelectAll.FirstOrDefault(), window.FindControl<MenuItem>("SelectAllMenuItem")!.InputGesture);
    }

    /// <summary>The mechanical parity guard the other tests above do not provide: none of them walk the full
    /// Keys or Edit submenu, so a dropped separator, a reordered item, a Command copied from the wrong line
    /// (a "_Connect"-headed item wired to DisconnectCommand) or — the case that matters most on a 3270 client —
    /// a CommandParameter copied from the wrong line (a "PA1"-headed item silently wired to TerminalKey.PA2)
    /// would pass every test above while still doing the wrong thing to the mainframe. This test walks all five
    /// top-level menus item for item — and recursively into their submenus — comparing header, separator
    /// position, Command and (on Keys) CommandParameter, normalising across the two menu kinds' different item
    /// types (MenuItem/NativeMenuItem) and separator types (Separator/NativeMenuItemSeparator).</summary>
    [AvaloniaFact]
    public void The_native_menu_matches_the_classic_menu_item_for_item()
    {
        var (window, _, _, _) = Show();

        var classicTop = window.FindControl<Menu>("ClassicMenu")!.Items.OfType<MenuItem>().ToArray();
        var nativeTop = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().ToArray();
        Assert.Equal(classicTop.Length, nativeTop.Length);

        for (var i = 0; i < classicTop.Length; i++)
        {
            var topHeader = (string)classicTop[i].Header!;
            Assert.Equal(topHeader, nativeTop[i].Header);
            AssertMenusMatch(topHeader, topHeader, classicTop[i].Items.Cast<object>().ToArray(), [.. nativeTop[i].Menu!.Items]);
        }
    }

    /// <summary>One level of the walk above, recursing into submenus — View &gt; Crosshair put four items a
    /// level deeper, and a submenu this did not descend into would be four items with no parity coverage at
    /// all. <paramref name="rootHeader"/> stays the *top-level* menu's header as the recursion descends,
    /// because the two exemptions below are properties of the top-level menu rather than of a depth.</summary>
    private static void AssertMenusMatch(string path, string rootHeader, object[] classicChildren, NativeMenuItemBase[] nativeChildren)
    {
        Assert.True(classicChildren.Length == nativeChildren.Length,
            $"{path}: {classicChildren.Length} classic items vs {nativeChildren.Length} native items");

        for (var j = 0; j < classicChildren.Length; j++)
        {
            var classicIsSeparator = classicChildren[j] is Separator;
            var nativeIsSeparator = nativeChildren[j] is NativeMenuItemSeparator;
            Assert.True(classicIsSeparator == nativeIsSeparator,
                $"{path}[{j}]: separator position differs (classic {classicIsSeparator}, native {nativeIsSeparator})");
            if (classicIsSeparator) continue;

            var classicItem = (MenuItem)classicChildren[j];
            var nativeItem = (NativeMenuItem)nativeChildren[j];
            Assert.Equal(classicItem.Header as string, nativeItem.Header);

            // Command, for every menu but Edit. Everything else checked here — headers, separator positions,
            // and the Keys CommandParameter check below — passes with "_Connect" bound to DisconnectCommand;
            // only this comparison sees it. Reference equality is the right test: both sides bind the same
            // [RelayCommand] instance off the one view model, and a null Command on both is what a Click-driven
            // item (New Session, File Transfer, Close, About, the crosshair modes) correctly looks like.
            //
            // Edit is a deliberate exception, not an oversight: its classic items bind Command (so the
            // [RelayCommand]s disable while running, which is correct for a mouse click on a menu item), while
            // its native items use Click handlers wired straight to the view model's methods — a native menu
            // gesture is a keystroke, and it must never be swallowed by a command disabled mid-round-trip. See
            // OnCopyClickNative and its neighbours in SessionWindow.axaml.cs.
            if (rootHeader != "_Edit")
            {
                Assert.True(ReferenceEquals(classicItem.Command, nativeItem.Command),
                    $"{path} > {classicItem.Header}: classic and native bind different commands");
            }

            // The highest-value assertion in this test. Every Keys item binds SendKeyCommand, so header text
            // alone cannot tell "PA1" wired to TerminalKey.PA1 apart from "PA1" wired to TerminalKey.PA2 — only
            // the CommandParameter can, and getting it wrong sends the wrong key to the mainframe.
            if (rootHeader == "_Keys")
            {
                Assert.Equal(classicItem.CommandParameter, nativeItem.CommandParameter);
            }

            // Both menus must agree about *where* the nesting is, before descending into it.
            var classicHasSubmenu = classicItem.Items.Count > 0;
            Assert.True(classicHasSubmenu == (nativeItem.Menu is not null),
                $"{path} > {classicItem.Header}: one menu nests a submenu here and the other does not");
            if (nativeItem.Menu is { } nativeSubmenu)
            {
                AssertMenusMatch($"{path} > {classicItem.Header}", rootHeader,
                    classicItem.Items.Cast<object>().ToArray(), [.. nativeSubmenu.Items]);
            }
        }
    }

    /// <summary>Gestures come from the platform hotkey table, not a hardcoded modifier, so macOS shows Cmd and
    /// the others Ctrl from the one table ShowPlatformGestures already reads for the classic menu.</summary>
    [AvaloniaFact]
    public void Only_the_edit_menu_carries_gestures()
    {
        var (window, _, _, _) = Show();
        var hotkeys = window.GetPlatformSettings()!.HotkeyConfiguration;

        Assert.Equal(hotkeys.Copy.FirstOrDefault(), Item(window, "_Edit", "_Copy").Gesture);
        Assert.Equal(hotkeys.Paste.FirstOrDefault(), Item(window, "_Edit", "_Paste").Gesture);
        Assert.Equal(hotkeys.SelectAll.FirstOrDefault(), Item(window, "_Edit", "Select _All").Gesture);

        // A gesture anywhere outside Edit is a 3270 client that cannot send that key, silently, with nothing in
        // the wire log: an AppKit key equivalent is dispatched ahead of the key window's responder chain, so
        // TerminalScreen never sees it. Walked exhaustively rather than from a list of examples — a list only
        // covers the items someone remembered to add to it, and View > Crosshair is precisely the submenu one
        // would have missed.
        foreach (var top in NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Where(i => i.Header != "_Edit"))
            AssertNoGestures(top.Header!, top.Menu!);
    }

    private static void AssertNoGestures(string path, NativeMenu menu)
    {
        foreach (var item in menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator))
        {
            Assert.True(item.Gesture is null,
                $"{path} > {item.Header} carries a gesture, which takes that key away from the terminal");
            if (item.Menu is { } submenu) AssertNoGestures($"{path} > {item.Header}", submenu);
        }
    }

    /// <summary>What this guards: with the native menu installed and its Edit gestures assigned (Task 6),
    /// Ctrl+V still reaches the host exactly once under both strategies, via
    /// <c>TerminalScreen.TryHandlePlatformGesture</c> alone. That is real coverage against two regressions — a
    /// future change that starts wiring a menu gesture to actual dispatch (switching <c>Gesture</c> to
    /// <c>MenuItem.HotKey</c>, which Avalonia's <c>HotKeyManager</c> does dispatch), and a break in
    /// <c>TerminalScreen</c>'s own clipboard routing.
    ///
    /// What it does NOT guard: decompiling the pinned Avalonia 12.1.2 <c>Avalonia.Controls.dll</c> shows that
    /// <c>NativeMenuBarPresenter.CreateContainerForNativeItem</c> — the in-window fallback bar used wherever a
    /// real AppKit <c>NSMenu</c> isn't exported, which includes headless — binds <c>NativeMenuItem.Gesture</c>
    /// only to <c>MenuItem.InputGestureProperty</c>, display text that <c>MenuItem.OnKeyDown</c> and
    /// <c>MenuBase.OnKeyDown</c> (both empty method bodies) never act on. So under headless, for both
    /// <c>InlineData</c> cases, there is no second dispatch path to catch even if one existed; a real AppKit
    /// key-equivalent interception (design section 2.1) needs a live macOS GUI session, and this test is not
    /// evidence about that path.</summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_paste_hotkey_reaches_the_host_exactly_once_via_TerminalScreen_under_headless(bool useNativeMenu)
    {
        var (window, _, session, clipboard) = Show(useNativeMenu);
        clipboard.Text = "claude";
        session.RaiseConnection(ConnectionState.Connected3270);

        window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);

        Assert.Equal(["paste:claude"], session.Calls);
    }

    /// <summary>#16: both are mapped in ActionMap and were reachable only from C#. The menu is the whole fix —
    /// keymap chords are a separate decision, since any chord has to clear the copy/paste/select-all gestures
    /// TerminalScreen checks before the keymap.</summary>
    [AvaloniaFact]
    public void Keys_menu_offers_Dup_and_FieldMark()
    {
        var (window, _, _, _) = Show();
        foreach (var header in new[] { "Dup", "Field Mark" })
        {
            Assert.NotNull(Item(window, "_Keys", header).Command);
        }
    }

    /// <summary>Capture needs no engine, so both items stay live with the session down. Gating them on
    /// IsConnected would take them away at the moment they are most wanted.</summary>
    [AvaloniaFact]
    public void The_capture_items_stay_enabled_while_disconnected()
    {
        var (window, _, session, _) = Show();
        session.RaiseConnection(ConnectionState.Disconnected);

        Assert.True(Item(window, "_File", "_Save Screen As...").IsEnabled);
        Assert.True(Item(window, "_Edit", "Copy Screen as _HTML").IsEnabled);
    }

    [AvaloniaFact]
    public void The_edit_menu_offers_find()
    {
        var (window, _, _, _) = Show();

        Assert.True(Item(window, "_Edit", "_Find...").IsEnabled);
    }

    /// <summary>Cmd+F on macOS, Ctrl+F elsewhere, built from the platform's CommandModifiers because
    /// PlatformHotkeyConfiguration carries no Find of its own (spec 5.3).</summary>
    [AvaloniaFact]
    public void Find_carries_the_platform_gesture()
    {
        var (window, _, _, _) = Show();
        // The same extension the production code uses; TopLevel.PlatformSettings is an explicit interface
        // implementation in 12.1.2 and is not reachable as a plain property.
        var expected = window.GetPlatformSettings()!.HotkeyConfiguration.CommandModifiers;

        var gesture = Item(window, "_Edit", "_Find...").Gesture;

        Assert.NotNull(gesture);
        Assert.Equal(Key.F, gesture!.Key);
        Assert.Equal(expected, gesture.KeyModifiers);
    }

    [AvaloniaFact]
    public void The_find_bar_opens_from_the_menu_and_focuses_its_box()
    {
        var (window, vm, _, _) = Show();

        ((INativeMenuItemExporterEventsImplBridge)Item(window, "_Edit", "_Find...")).RaiseClicked();

        Assert.True(vm.Find.IsOpen);
        Assert.True(window.FindControl<TextBox>("FindBox")!.IsFocused);
    }

    /// <summary>Escape closes the bar and hands focus back, the same path the error bar's Dismiss uses.</summary>
    [AvaloniaFact]
    public void Escape_closes_the_find_bar_and_returns_focus_to_the_screen()
    {
        var (window, vm, _, _) = Show();
        ((INativeMenuItemExporterEventsImplBridge)Item(window, "_Edit", "_Find...")).RaiseClicked();

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.False(vm.Find.IsOpen);
        Assert.True(window.FindControl<TerminalScreen>("Screen")!.IsFocused);
    }

    /// <summary>Matches reach the control, so the overlay has something to paint. Show()'s fixture seeds "hello"
    /// at row 2 column 3, so asserting a single match first is what stops this passing vacuously if
    /// ScreenSearch.Find regressed to returning nothing — both sides would still be empty/null and the two
    /// Equal calls below would not notice.</summary>
    [AvaloniaFact]
    public void Find_matches_reach_the_terminal_screen()
    {
        var (window, vm, _, _) = Show();
        vm.Find.Open();

        vm.Find.Term = "hello";

        Assert.Single(vm.Find.Matches);
        var screen = window.FindControl<TerminalScreen>("Screen")!;
        Assert.Equal(vm.Find.Matches, screen.FindMatches);
        Assert.Equal(vm.Find.CurrentMatch, screen.CurrentMatch);
    }

    /// <summary>What none of the tests above cover: every one of them reaches the find bar through
    /// <c>RaiseClicked</c> or by poking the view model directly, never through the control. That matters because
    /// under the classic menu strategy — the default on Windows and Linux — the native menu is emptied and
    /// <c>MenuItem.InputGesture</c> is display only (see the decompilation note on
    /// <see cref="The_paste_hotkey_reaches_the_host_exactly_once_via_TerminalScreen_under_headless"/>), so
    /// <c>TerminalScreen.TryHandlePlatformGesture</c> → <c>FindRequested</c> → <c>ShowFind</c> is the *only*
    /// dispatch path that exists there. Modelled on that same paste test, over both menu strategies for the same
    /// reason: this path does not go through either menu at all, so both must reach the bar identically.</summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_find_gesture_opens_the_bar_via_TerminalScreen(bool useNativeMenu)
    {
        var (window, vm, _, _) = Show(useNativeMenu);
        window.FindControl<TerminalScreen>("Screen")!.Focus();

        window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control);

        Assert.True(vm.Find.IsOpen);
        Assert.True(window.FindControl<TextBox>("FindBox")!.IsFocused);
    }

    /// <summary>Item 2: on macOS these three native Edit items are AppKit key equivalents that
    /// <c>NSApplication.sendEvent:</c> dispatches ahead of the key window's responder chain (see
    /// <c>OnPasteClickNative</c> and its neighbours in SessionWindow.axaml.cs). Task 8 gave this window its
    /// first focusable text field, which makes the find box reachable by them, so each handler must route to
    /// the box instead of the session while it is focused. Driven through <c>RaiseClicked</c>, the entry point
    /// both real renderers use.
    ///
    /// The clipboard used here is Avalonia's own (<c>window.Clipboard</c>, what <c>TextBox.Paste</c>/<c>Copy</c>
    /// read and write), which is a different store from the <c>FakeTextClipboard</c> the session's
    /// Copy/PasteAsync go through — the two must not be confused for this test to mean anything.</summary>
    [AvaloniaFact]
    public async Task The_native_paste_item_pastes_into_the_find_box_when_it_is_focused()
    {
        var (window, _, session, clipboard) = Show();
        session.RaiseConnection(ConnectionState.Connected3270);
        // What PasteAsync would send to the host if the focus guard were missing.
        clipboard.Text = "typed-into-the-host-by-mistake";
        ((INativeMenuItemExporterEventsImplBridge)Item(window, "_Edit", "_Find...")).RaiseClicked();
        await window.Clipboard!.SetTextAsync("claude");

        ((INativeMenuItemExporterEventsImplBridge)Item(window, "_Edit", "_Paste")).RaiseClicked();

        Assert.Equal("claude", window.FindControl<TextBox>("FindBox")!.Text);
        Assert.DoesNotContain(session.Calls, call => call.StartsWith("paste:"));
    }

    /// <inheritdoc cref="The_native_paste_item_pastes_into_the_find_box_when_it_is_focused"/>
    [AvaloniaFact]
    public async Task The_native_copy_item_copies_the_find_box_text_when_it_is_focused()
    {
        var (window, vm, _, _) = Show();
        // The terminal has something to copy too, so a guard-free handler would have somewhere else to act.
        vm.Selection = ScreenRegion.FromCorners(2, 3, 2, 7);
        ((INativeMenuItemExporterEventsImplBridge)Item(window, "_Edit", "_Find...")).RaiseClicked();
        var box = window.FindControl<TextBox>("FindBox")!;
        box.Text = "needle";
        box.SelectAll();

        ((INativeMenuItemExporterEventsImplBridge)Item(window, "_Edit", "_Copy")).RaiseClicked();

        Assert.Equal("needle", await window.Clipboard!.TryGetTextAsync());
    }

    /// <inheritdoc cref="The_native_paste_item_pastes_into_the_find_box_when_it_is_focused"/>
    [AvaloniaFact]
    public void The_native_select_all_item_selects_the_find_box_text_when_it_is_focused()
    {
        var (window, vm, _, _) = Show();
        ((INativeMenuItemExporterEventsImplBridge)Item(window, "_Edit", "_Find...")).RaiseClicked();
        var box = window.FindControl<TextBox>("FindBox")!;
        box.Text = "hello";

        ((INativeMenuItemExporterEventsImplBridge)Item(window, "_Edit", "Select _All")).RaiseClicked();

        Assert.Equal(0, box.SelectionStart);
        Assert.Equal(5, box.SelectionEnd);
        Assert.Null(vm.Selection);
    }
}
