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
using Avalonia.Interactivity;
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

    /// <summary>macOS by default, because that is the only platform where the three styles differ: Resolve
    /// answers InWindow for every one of them elsewhere, so a window built with the runner's own platform
    /// would ignore the style a test names. Ask for the other shape by name where it is what is under test.</summary>
    private static (SessionWindow Window, SessionViewModel Vm, FakeEmulatorSession Session, FakeTextClipboard Clipboard) Show(MenuStyle style = MenuStyle.Native, bool isMacOS = true)
    {
        var session = new FakeEmulatorSession();
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(2, 3, "hello", null, null, null);
        session.CurrentScreen = buffer.Snapshot();
        var clipboard = new FakeTextClipboard();
        var vm = new SessionViewModel(session, action => action(), clipboard);
        var window = new SessionWindow(style, isMacOS) { DataContext = vm };
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

    /// <summary>So do the keypad's, for the same reason turned around: the dock belongs beside the thing it
    /// docks, and View is not where "At the Bottom" means anything on its own (#71).</summary>
    private static NativeMenuItem KeypadItem(SessionWindow window, string header) =>
        MenuLookup.Item(Item(window, "_View", "_Keypad").Menu, header)
        ?? throw new InvalidOperationException($"no native menu item _View > _Keypad > {header}");

    [AvaloniaFact]
    public void The_window_menu_has_the_same_six_top_level_menus_as_the_classic_one()
    {
        var (window, _, _, _) = Show();

        var headers = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Select(i => i.Header!).ToArray();
        Assert.Equal(["_File", "_Edit", "_View", "_Keys", "_Window", "_Help"], headers);
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
        Assert.Equal(CrosshairMode.None, vm.Settings.Crosshair);

        // None last, not first. It is the default, so clicking it first would assert the state the test began in
        // and an item wired to the wrong handler would pass on it. In this order every click has to change the
        // mode. (#71 found the same hole in the keypad dock's test, which this one was the template for.)
        foreach (var chosen in new[] { 1, 2, 3, 0 })
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

    /// <summary>View > Keypad > Show the Keypad is a check box in the Crosshair items' shape on both menus
    /// (keypad spec §6.3): RaiseClicked is the entry point both real renderers use, the handler flips the
    /// setting, and the one-way bindings carry the mark back to both items. The classic item is driven through
    /// its own Click for the same reason. No gesture: nothing outside Edit carries one.</summary>
    [AvaloniaFact]
    public void Clicking_view_keypad_flips_the_setting_and_the_check_mark_on_both_menus()
    {
        var (window, vm, _, _) = Show();
        var native = KeypadItem(window, "_Show the Keypad");
        var classic = window.FindControl<MenuItem>("KeypadShowMenuItem")!;
        Assert.Equal(MenuItemToggleType.CheckBox, native.ToggleType);
        Assert.Null(native.Gesture);
        Assert.False(native.IsChecked);
        Assert.False(classic.IsChecked);

        ((INativeMenuItemExporterEventsImplBridge)native).RaiseClicked();
        Assert.True(vm.Settings.Keypad);
        Assert.True(native.IsChecked);
        Assert.True(classic.IsChecked);

        classic.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.False(vm.Settings.Keypad);
        Assert.False(native.IsChecked);
        Assert.False(classic.IsChecked);
    }

    /// <summary>The dock radios in the same submenu, in the Crosshair radios' shape (#71). Both docks are
    /// exercised on each menu rather than one anywhere: KeypadDockConverter.Convert throws on a parameter that
    /// does not parse, but Avalonia swallows a converter's exception, so a mistyped ConverterParameter would
    /// surface only as a wrong IsChecked on that one item's own click. Each of the four items is clicked and each
    /// check mark is asserted on both menus, because the two renderers bind the same converter through four
    /// separate handlers and any one of them can be miswired on its own. Nothing here writes the settings file:
    /// SettingsViewModel.KeypadDock does that, and the menu is a second door onto it.</summary>
    [AvaloniaFact]
    public void Choosing_a_keypad_dock_checks_exactly_that_item_on_both_menus()
    {
        var (window, vm, _, _) = Show();
        const int bottom = 0, right = 1;
        var native = new[] { "At the _Bottom", "On the _Right" }.Select(h => KeypadItem(window, h)).ToArray();
        var classic = new[] { "KeypadDockBottomMenuItem", "KeypadDockRightMenuItem" }
            .Select(name => window.FindControl<MenuItem>(name)!).ToArray();
        Assert.All(native, item => Assert.Equal(MenuItemToggleType.Radio, item.ToggleType));
        Assert.All(native, item => Assert.Null(item.Gesture));
        // The classic side needs both, and neither is compared by the parity walk: a radio left as a CheckBox
        // with no group would tick independently of its sibling on Windows and Linux, where this is the only bar.
        Assert.All(classic, item => Assert.Equal(MenuItemToggleType.Radio, item.ToggleType));
        Assert.All(classic, item => Assert.Equal("KeypadDock", item.GroupName));
        Assert.Equal(KeypadDock.Bottom, vm.Settings.KeypadDock);
        Assert.False(vm.Settings.Keypad);

        // Away from the default and back, on each menu in turn, so every click has to *change* the dock to pass.
        // Starting on Bottom and clicking Bottom would assert the state it began in, and an item wired to the
        // other dock's handler — or to ToggleKeypad — would sail through it.
        foreach (var (item, chosen) in new (Action, int)[]
                 {
                     (() => ((INativeMenuItemExporterEventsImplBridge)native[right]).RaiseClicked(), right),
                     (() => ((INativeMenuItemExporterEventsImplBridge)native[bottom]).RaiseClicked(), bottom),
                     (() => classic[right].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)), right),
                     (() => classic[bottom].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)), bottom),
                 })
        {
            item();

            Assert.Equal(chosen == bottom ? KeypadDock.Bottom : KeypadDock.Right, vm.Settings.KeypadDock);
            Assert.Equal([chosen == bottom, chosen == right], native.Select(i => i.IsChecked));
            Assert.Equal([chosen == bottom, chosen == right], classic.Select(i => i.IsChecked));
            Assert.False(vm.Settings.Keypad); // A dock item wired to ToggleKeypad would show up here.
        }
    }

    /// <summary>The submenu's shape, asserted on both renderers because the parity walk compares structure and
    /// this is the structure it compares (#71). A separator between the toggle and the radios, because these are
    /// two settings and not one enum: keypad spec §2.1 rejected a single enum with a Hidden member, since it
    /// would forget the dock every time the keypad was hidden.</summary>
    [AvaloniaFact]
    public void The_keypad_submenu_carries_the_toggle_above_its_dock_on_both_menus()
    {
        var (window, _, _, _) = Show();
        var expected = new[] { "_Show the Keypad", "At the _Bottom", "On the _Right" };

        var nativeKeypad = Item(window, "_View", "_Keypad");
        var nativeChildren = nativeKeypad.Menu!.Items;
        Assert.Equal(expected, nativeChildren.OfType<NativeMenuItem>()
            .Where(i => i is not NativeMenuItemSeparator).Select(i => i.Header));
        Assert.IsType<NativeMenuItemSeparator>(nativeChildren[1]);

        var classicKeypad = window.FindControl<Menu>("ClassicMenu")!.Items.OfType<MenuItem>()
            .Single(i => (string)i.Header! == "_View").Items.OfType<MenuItem>()
            .Single(i => (string)i.Header! == "_Keypad");
        Assert.Equal(expected, classicKeypad.Items.OfType<MenuItem>().Select(i => (string)i.Header!));
        Assert.IsType<Separator>(classicKeypad.Items[1]);
    }

    /// <summary>The keypad offers at least what the menu does (keypad spec §3), read from the menu itself so the
    /// two cannot drift: a key added to the Keys menu and not to KeypadLayout fails here.</summary>
    [AvaloniaFact]
    public void Every_key_on_the_Keys_menu_is_on_the_keypad()
    {
        var (window, _, _, _) = Show();
        var onKeypad = KeypadLayout.Banks.SelectMany(bank => bank).Select(k => k.Key).ToHashSet();
        var onMenu = MenuLookup.Item(NativeMenu.GetMenu(window), "_Keys")!.Menu!.Items
            .OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator)
            .Select(i => (TerminalKey)i.CommandParameter!).ToArray();

        Assert.NotEmpty(onMenu);
        Assert.All(onMenu, key => Assert.Contains(key, onKeypad));
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
        Assert.Equal(["_Crosshair", "_Keypad"], nativeChildren.Select(i => i.Header));
        Assert.Equal(expected, nativeChildren[0].Menu!.Items.OfType<NativeMenuItem>().Select(i => i.Header));

        var classicView = window.FindControl<Menu>("ClassicMenu")!.Items.OfType<MenuItem>()
            .Single(i => (string)i.Header! == "_View");
        var classicChildren = classicView.Items.OfType<MenuItem>().ToArray();
        Assert.Equal(["_Crosshair", "_Keypad"], classicChildren.Select(i => (string)i.Header!));
        Assert.Equal(expected, classicChildren[0].Items.OfType<MenuItem>().Select(i => (string)i.Header!));
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

    /// <summary>The generated session rows are leaves too, and each needs its Click handler.</summary>
    [AvaloniaFact]
    public void Every_native_item_can_actually_be_activated_with_sessions_open()
    {
        var (window, vm, _, _) = Show();
        TestSessions.Attach(window, vm, "CONSOLE", "IMON");

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

    /// <summary>The exported menu only; the in-window one follows no platform rule (#103, below). On a CI machine
    /// this covers the visible-in-Help branch only, and the macOS branch runs on a Mac, where running the app is
    /// what the spec's section 8 requires anyway. No mutable platform static is introduced to close that gap — it
    /// would make every menu test order-dependent.</summary>
    [AvaloniaFact]
    public void About_is_in_the_exported_help_menu_on_this_platform_exactly_when_the_strategy_says_so()
    {
        var (window, _, _, _) = Show();
        var expected = MenuStrategy.AboutInExportedHelpMenu(OperatingSystem.IsMacOS());

        Assert.Equal(expected, Item(window, "_Help", "_About LizTerm...").IsVisible);
    }

    /// <summary>And the divider above it goes too. Nothing collapses a trailing separator in NSMenu, so hiding
    /// About on its own leaves the macOS Help menu ending in a line with nothing under it. The exporter honours
    /// IsVisible on a separator because NativeMenuItemSeparator derives from NativeMenuItem.</summary>
    [AvaloniaFact]
    public void The_separator_above_the_exported_about_is_hidden_with_it()
    {
        var (window, _, _, _) = Show();
        var expected = MenuStrategy.AboutInExportedHelpMenu(OperatingSystem.IsMacOS());

        var separator = MenuLookup.SeparatorAbove(Item(window, "_Help", "_About LizTerm..."));
        Assert.NotNull(separator);
        Assert.Equal(expected, separator!.IsVisible);
    }

    /// <summary>The same shape as About in Help, with the same coverage.</summary>
    [AvaloniaFact]
    public void Preferences_is_in_the_exported_edit_menu_on_this_platform_exactly_when_the_strategy_says_so()
    {
        var (window, _, _, _) = Show();
        var expected = MenuStrategy.PreferencesInExportedEditMenu(OperatingSystem.IsMacOS());

        Assert.Equal(expected, Item(window, "_Edit", "P_references...").IsVisible);
    }

    [AvaloniaFact]
    public void The_separator_above_the_exported_preferences_is_hidden_with_it()
    {
        var (window, _, _, _) = Show();
        var expected = MenuStrategy.PreferencesInExportedEditMenu(OperatingSystem.IsMacOS());

        var separator = MenuLookup.SeparatorAbove(Item(window, "_Edit", "P_references..."));
        Assert.NotNull(separator);
        Assert.Equal(expected, separator!.IsVisible);
    }

    /// <summary>#103. A user who picks the in-window menu has stopped looking at the system menu bar, and on macOS
    /// the application menu in that bar was the only place Preferences appeared, so the setting that hid the bar
    /// left no visible way back to itself. The in-window menu therefore carries About and Preferences, with their
    /// separators, in every style, as it does on every other platform; Native hides the whole bar, so nothing
    /// changes on screen there. A CI runner passed every case before #103 as well. On a Mac, where the old rule
    /// read the real platform, every case failed.</summary>
    [AvaloniaTheory]
    [InlineData(MenuStyle.Native)]
    [InlineData(MenuStyle.InWindow)]
    [InlineData(MenuStyle.Both)]
    public void The_in_window_menu_carries_about_and_preferences_in_every_style(MenuStyle style) =>
        AssertInWindowAboutAndPreferencesShown(Show(style).Window, $"opened as {style}");

    /// <summary>A style change re-runs ApplyPlatformMenuRules, so the in-window items have to survive every
    /// transition and not only the style a window opened with. The sequence walks all six.</summary>
    [AvaloniaFact]
    public void The_in_window_about_and_preferences_survive_every_live_style_change()
    {
        var (window, vm, _, _) = Show(MenuStyle.Native);

        foreach (var style in new[] { MenuStyle.InWindow, MenuStyle.Native, MenuStyle.Both, MenuStyle.InWindow, MenuStyle.Both, MenuStyle.Native })
        {
            vm.Settings.MenuStyle = style;
            AssertInWindowAboutAndPreferencesShown(window, $"after switching to {style}");
        }
    }

    private static void AssertInWindowAboutAndPreferencesShown(SessionWindow window, string when)
    {
        Assert.True(window.FindControl<MenuItem>("PreferencesMenuItem")!.IsVisible, $"Edit > Preferences... hidden {when}");
        Assert.True(window.FindControl<Separator>("PreferencesSeparator")!.IsVisible, $"the separator above Preferences hidden {when}");
        Assert.True(window.FindControl<MenuItem>("AboutMenuItem")!.IsVisible, $"Help > About LizTerm... hidden {when}");
        Assert.True(window.FindControl<Separator>("AboutSeparator")!.IsVisible, $"the separator above About hidden {when}");
    }

    /// <summary>The in-window Preferences item names the chord that opens Preferences, and that chord is the
    /// application menu's: the application menu is there under every style, so the chord works whichever window
    /// menu is drawn, and a user who has lost the system menu bar can read the way back off the window. A label
    /// and nothing more — MenuItem.InputGesture is display-only in 12.1.2, so the application menu's key
    /// equivalent stays the one thing that acts on the chord. Off macOS there is no application menu and no
    /// chord to name.</summary>
    [AvaloniaFact]
    public void The_in_window_preferences_item_names_the_application_menu_chord_on_macOS_only()
    {
        var (window, _, _, _) = Show(MenuStyle.InWindow);
        var applicationPreferences = MenuLookup.Item(NativeMenu.GetMenu(Application.Current!), "Preferences...")!;

        var expected = OperatingSystem.IsMacOS() ? applicationPreferences.Gesture : null;

        Assert.Equal(expected, window.FindControl<MenuItem>("PreferencesMenuItem")!.InputGesture);
    }

    [AvaloniaFact]
    public void Preferences_is_the_last_edit_item_in_both_menus()
    {
        var (window, _, _, _) = Show();

        Assert.Equal("P_references...", Item(window, "_Edit", "P_references...").Parent!.Items.OfType<NativeMenuItem>().Last().Header);
        var classicEdit = window.FindControl<Menu>("ClassicMenu")!.Items.OfType<MenuItem>().Single(m => (string)m.Header! == "_Edit");
        Assert.Equal("P_references...", (string)classicEdit.Items.OfType<MenuItem>().Last().Header!);
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
    public void Each_style_shows_the_renderers_it_names()
    {
        var (native, _, _, _) = Show(MenuStyle.Native);
        Assert.False(native.FindControl<Menu>("ClassicMenu")!.IsVisible);
        Assert.True(native.FindControl<NativeMenuBar>("NativeBar")!.IsVisible);

        var (classic, _, _, _) = Show(MenuStyle.InWindow);
        Assert.True(classic.FindControl<Menu>("ClassicMenu")!.IsVisible);
        Assert.False(classic.FindControl<NativeMenuBar>("NativeBar")!.IsVisible);

        var (both, _, _, _) = Show(MenuStyle.Both);
        Assert.True(both.FindControl<Menu>("ClassicMenu")!.IsVisible);
        Assert.True(both.FindControl<NativeMenuBar>("NativeBar")!.IsVisible);
    }

    /// <summary>Both is reached by not suppressing it: the classic bar stays visible and the declared NativeMenu
    /// stays populated, which is the state ApplyMenuStyle goes out of its way to prevent under Native and
    /// InWindow. Measured on macOS, that draws an in-window bar beneath the system one.</summary>
    [AvaloniaFact]
    public void Both_keeps_the_native_menu_populated_alongside_the_classic_bar()
    {
        var (window, _, _, _) = Show(MenuStyle.Both);

        Assert.NotEmpty(NativeMenu.GetMenu(window)!.Items);
        Assert.True(window.FindControl<Menu>("ClassicMenu")!.IsVisible);
    }

    /// <summary>The preference applies to open windows, so InWindow has to be a state the window can leave as
    /// well as enter. Emptying the declared menu is destructive — the items are removed and their Parent nulled —
    /// so ApplyMenuStyle stashes them and adds the same objects back to the same instance. The same instance is
    /// the part that matters (#60): Avalonia's macOS exporter binds its native proxy to the first NativeMenu a
    /// window is given and throws for any other, so a refill that built a new menu would pass here and throw on
    /// a Mac. Assert.Same is what says it did not.</summary>
    [AvaloniaFact]
    public void A_style_change_on_an_open_window_empties_and_refills_the_same_native_menu()
    {
        var (window, vm, _, _) = Show(MenuStyle.Native);
        var declared = NativeMenu.GetMenu(window)!;
        var headers = declared.Items.OfType<NativeMenuItem>().Select(i => i.Header).ToList();
        Assert.NotEmpty(headers);

        vm.Settings.MenuStyle = MenuStyle.InWindow;
        Assert.Empty(declared.Items);
        Assert.True(window.FindControl<Menu>("ClassicMenu")!.IsVisible);

        vm.Settings.MenuStyle = MenuStyle.Both;
        Assert.Equal(headers, declared.Items.OfType<NativeMenuItem>().Select(i => i.Header));
        Assert.True(window.FindControl<Menu>("ClassicMenu")!.IsVisible);

        vm.Settings.MenuStyle = MenuStyle.Native;
        Assert.Equal(headers, declared.Items.OfType<NativeMenuItem>().Select(i => i.Header));
        Assert.False(window.FindControl<Menu>("ClassicMenu")!.IsVisible);
        Assert.Same(declared, NativeMenu.GetMenu(window));
    }

    /// <summary>The direction that needs ApplyMenuStyle's re-run of ApplyPlatformMenuRules and
    /// ShowPlatformGestures, and the one the test above cannot see: a window that *opened* under InWindow had its
    /// menu emptied before either ever ran against it, so the items come back never having been given a gesture
    /// or had the About/Preferences rule applied. Headers survive a refill whatever those two calls do — they are
    /// XAML literals — so this asserts the state a refill has to restore rather than the state it cannot
    /// lose. Delete either call from ApplyMenuStyle and this is what fails.</summary>
    [AvaloniaFact]
    public void A_window_opened_in_window_gets_its_gestures_and_platform_rules_when_it_switches_to_native()
    {
        var (window, vm, _, _) = Show(MenuStyle.InWindow);
        var declared = NativeMenu.GetMenu(window)!;
        Assert.Empty(declared.Items);

        vm.Settings.MenuStyle = MenuStyle.Native;

        var hotkeys = window.GetPlatformSettings()!.HotkeyConfiguration;
        Assert.Equal(hotkeys.Copy.FirstOrDefault(), MenuLookup.Item(declared, "_Edit", "_Copy")!.Gesture);
        Assert.Equal(hotkeys.Paste.FirstOrDefault(), MenuLookup.Item(declared, "_Edit", "_Paste")!.Gesture);
        Assert.Equal(hotkeys.SelectAll.FirstOrDefault(), MenuLookup.Item(declared, "_Edit", "Select _All")!.Gesture);
        Assert.Equal(new KeyGesture(Key.F, hotkeys.CommandModifiers), MenuLookup.Item(declared, "_Edit", "_Find...")!.Gesture);

        // The application menu supplies both on macOS, so the exported copy may not show a second.
        Assert.Equal(
            MenuStrategy.AboutInExportedHelpMenu(OperatingSystem.IsMacOS()),
            MenuLookup.Item(declared, "_Help", "_About LizTerm...")!.IsVisible);
        Assert.Equal(
            MenuStrategy.PreferencesInExportedEditMenu(OperatingSystem.IsMacOS()),
            MenuLookup.Item(declared, "_Edit", "P_references...")!.IsVisible);
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
        var (native, _, _, _) = Show(MenuStyle.Native);
        Assert.NotEmpty(NativeMenu.GetMenu(native)!.Items);

        var (classic, _, _, _) = Show(MenuStyle.InWindow);
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
        var (window, _, _, _) = Show(MenuStyle.InWindow);
        var hotkeys = window.GetPlatformSettings()!.HotkeyConfiguration;

        Assert.Equal(hotkeys.Copy.FirstOrDefault(), window.FindControl<MenuItem>("CopyMenuItem")!.InputGesture);
        Assert.Equal(hotkeys.Paste.FirstOrDefault(), window.FindControl<MenuItem>("PasteMenuItem")!.InputGesture);
        Assert.Equal(hotkeys.SelectAll.FirstOrDefault(), window.FindControl<MenuItem>("SelectAllMenuItem")!.InputGesture);
    }

    /// <summary>The mechanical parity guard the other tests above do not provide: none of them walk the full
    /// Keys or Edit submenu, so a dropped separator, a reordered item, a Command copied from the wrong line
    /// (a "_Connect"-headed item wired to DisconnectCommand) or — the case that matters most on a 3270 client —
    /// a CommandParameter copied from the wrong line (a "PA1"-headed item silently wired to TerminalKey.PA2)
    /// would pass every test above while still doing the wrong thing to the mainframe. This test walks all six
    /// top-level menus item for item — and recursively into their submenus — comparing header, separator
    /// position, Command and (on Keys) CommandParameter, normalising across the two menu kinds' different item
    /// types (MenuItem/NativeMenuItem) and separator types (Separator/NativeMenuItemSeparator).</summary>
    [AvaloniaFact]
    public void The_native_menu_matches_the_classic_menu_item_for_item() => AssertParity(Show().Window);

    /// <summary>The session rows are built in code, in both menus from one list; with sessions open the item-for-item
    /// walk must still hold (session switching spec §10.2).</summary>
    [AvaloniaFact]
    public void The_native_menu_matches_the_classic_menu_item_for_item_with_sessions_open()
    {
        var (window, vm, _, _) = Show();
        TestSessions.Attach(window, vm, "CONSOLE", "IMON");

        AssertParity(window);
    }

    private static void AssertParity(SessionWindow window)
    {
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

    /// <summary>The parity walk compares CommandParameter, which for Help is the whole of an item's meaning:
    /// every link binds the same OpenLinkCommand, so a native item pointed at the wrong page differs from its
    /// classic twin in nothing else. This breaks a Help item deliberately, because a guard that cannot fail is
    /// not a guard.</summary>
    [AvaloniaFact]
    public void The_parity_walk_compares_command_parameters_outside_the_keys_menu()
    {
        var (window, _, _, _) = Show();
        Item(window, "_Help", "_Wire Log").CommandParameter = "deliberately different";

        // Record.Exception rather than Assert.Throws<EqualException>: what matters is that the walk rejects
        // this, not which assertion inside it happened to fire first.
        Assert.NotNull(Record.Exception(() => AssertParity(window)));
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

            // Every Keys item binds SendKeyCommand and every Help link binds OpenLinkCommand, so header text
            // alone cannot tell "PA1" wired to TerminalKey.PA1 apart from "PA1" wired to TerminalKey.PA2, nor
            // "Releases" pointed at the issue tracker. Only the CommandParameter can, and getting either wrong
            // is invisible in the menu itself.
            if (rootHeader is "_Keys" or "_Help")
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
    /// the others Ctrl from the one table ShowPlatformGestures already reads for the classic menu. Outside Edit
    /// exactly two items carry one, both Cmd chords no 3270 keystroke uses (session switching spec §6):
    /// Window &gt; Switch Session... and, on macOS, Window &gt; Minimize.</summary>
    [AvaloniaFact]
    public void Only_edit_and_two_window_items_carry_gestures()
    {
        var (window, _, _, _) = Show();
        var hotkeys = window.GetPlatformSettings()!.HotkeyConfiguration;

        Assert.Equal(hotkeys.Copy.FirstOrDefault(), Item(window, "_Edit", "_Copy").Gesture);
        Assert.Equal(hotkeys.Paste.FirstOrDefault(), Item(window, "_Edit", "_Paste").Gesture);
        Assert.Equal(hotkeys.SelectAll.FirstOrDefault(), Item(window, "_Edit", "Select _All").Gesture);
        Assert.Equal(new KeyGesture(Key.K, hotkeys.CommandModifiers), Item(window, "_Window", "_Switch Session...").Gesture);
        Assert.Equal(new KeyGesture(Key.M, KeyModifiers.Meta), Item(window, "_Window", "_Minimize").Gesture);

        // A gesture anywhere else is a 3270 client that cannot send that key, silently, with nothing in the wire
        // log: an AppKit key equivalent is dispatched ahead of the key window's responder chain, so TerminalScreen
        // never sees it. Walked exhaustively rather than from a list of examples — a list only covers the items
        // someone remembered to add to it, and View > Crosshair is precisely the submenu one would have missed.
        foreach (var top in NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Where(i => i.Header != "_Edit"))
            AssertNoGestures(top.Header!, top.Menu!);
    }

    private static readonly string[] GestureExceptions = ["_Window > _Switch Session...", "_Window > _Minimize"];

    private static void AssertNoGestures(string path, NativeMenu menu)
    {
        foreach (var item in menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator))
        {
            var itemPath = $"{path} > {item.Header}";
            if (GestureExceptions.Contains(itemPath)) continue;
            Assert.True(item.Gesture is null,
                $"{itemPath} carries a gesture, which takes that key away from the terminal");
            if (item.Menu is { } submenu) AssertNoGestures(itemPath, submenu);
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
    [InlineData(MenuStyle.Native)]
    [InlineData(MenuStyle.InWindow)]
    [InlineData(MenuStyle.Both)]
    public void The_paste_hotkey_reaches_the_host_exactly_once_via_TerminalScreen_under_headless(MenuStyle style)
    {
        var (window, _, session, clipboard) = Show(style);
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

    /// <summary>#111: Insert was on the Insert key alone, which Apple's keyboards do not have. The Keys menu is the
    /// door every keyboard has, so a click on it must reach the host as the insert toggle.</summary>
    [AvaloniaFact]
    public void Keys_menu_insert_sends_the_insert_toggle()
    {
        var (window, _, session, _) = Show();

        ((INativeMenuItemExporterEventsImplBridge)Item(window, "_Keys", "Insert")).RaiseClicked();

        Assert.Equal(["key:Insert"], session.Calls);
    }

    /// <summary>#111: Insert is a check box on both menus, and its mark is the host's insert state, the one the status
    /// bar's caret shows, so Ctrl+I, the Insert key and the keypad move it too.</summary>
    [AvaloniaFact]
    public void Keys_menu_insert_is_checked_while_the_host_reports_insert_mode_on_both_menus()
    {
        var (window, _, session, _) = Show();
        var native = Item(window, "_Keys", "Insert");
        var classic = window.FindControl<MenuItem>("InsertMenuItem");
        Assert.NotNull(classic);
        Assert.Equal(MenuItemToggleType.CheckBox, native.ToggleType);
        Assert.Equal(MenuItemToggleType.CheckBox, classic.ToggleType);
        Assert.False(native.IsChecked);
        Assert.False(classic.IsChecked);

        session.RaiseStatus(new KeyboardStatus(KeyboardLock.Unlocked, null, true, false, null));
        Assert.True(native.IsChecked);
        Assert.True(classic.IsChecked);

        session.RaiseStatus(new KeyboardStatus(KeyboardLock.Unlocked, null, false, false, null));
        Assert.False(native.IsChecked);
        Assert.False(classic.IsChecked);
    }

    /// <summary>#111: a click sends the toggle and leaves the mark to the host's answer. Both renderers write IsChecked
    /// before the click reaches the item (DefaultMenuInteractionHandler.Click on the classic MenuItem, and the
    /// in-window NativeMenuBar fallback through its two-way binding to the NativeMenuItem), so each is replayed in that
    /// order. Left alone, that write would show insert mode on before the host said so, and stay wrong if the host
    /// never did.</summary>
    [AvaloniaFact]
    public void Clicking_keys_menu_insert_leaves_the_check_mark_to_the_host_on_both_menus()
    {
        var (window, _, session, _) = Show();
        var native = Item(window, "_Keys", "Insert");
        var classic = window.FindControl<MenuItem>("InsertMenuItem");
        Assert.NotNull(classic);

        native.IsChecked = true;
        ((INativeMenuItemExporterEventsImplBridge)native).RaiseClicked();
        classic.IsChecked = true;
        classic.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(["key:Insert", "key:Insert"], session.Calls);
        Assert.False(native.IsChecked);
        Assert.False(classic.IsChecked);

        // Both marks still follow the host after the corrections: neither write cost an item its binding.
        session.RaiseStatus(new KeyboardStatus(KeyboardLock.Unlocked, null, true, false, null));
        Assert.True(native.IsChecked);
        Assert.True(classic.IsChecked);
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
    [InlineData(MenuStyle.Native)]
    [InlineData(MenuStyle.InWindow)]
    [InlineData(MenuStyle.Both)]
    public void The_find_gesture_opens_the_bar_via_TerminalScreen(MenuStyle style)
    {
        var (window, vm, _, _) = Show(style);
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

    [AvaloniaFact]
    public void Help_offers_the_three_project_links_on_both_menus()
    {
        var (window, _, _, _) = Show();
        var classic = window.FindControl<Menu>("ClassicMenu")!.Items.OfType<MenuItem>()
            .Single(m => (string)m.Header! == "_Help");

        foreach (var (header, url) in new[]
                 {
                     ("Project on _GitHub", ProjectLinks.Repository),
                     ("_Report an Issue...", ProjectLinks.NewIssue),
                     ("R_eleases", ProjectLinks.Releases),
                 })
        {
            var native = Item(window, "_Help", header);
            Assert.Equal(url, native.CommandParameter);
            Assert.NotNull(native.Command);

            var classicItem = classic.Items.OfType<MenuItem>().Single(m => (string)m.Header! == header);
            Assert.Equal(url, classicItem.CommandParameter);
        }
    }

    [AvaloniaFact]
    public async Task Choosing_a_project_link_opens_it()
    {
        var opener = new FakeUriOpener();
        var vm = new SessionViewModel(new FakeEmulatorSession(), action => action(), new FakeTextClipboard(),
            uriOpener: opener);
        var window = new SessionWindow(MenuStyle.Native, isMacOS: true) { DataContext = vm };
        window.Show();

        ((INativeMenuItemExporterEventsImplBridge)Item(window, "_Help", "_Report an Issue...")).RaiseClicked();
        await Wait.UntilAsync(() => opener.Opened.Count == 1, "the link to be opened");

        Assert.Equal([ProjectLinks.NewIssue], opener.Opened);
    }

    [AvaloniaFact]
    public void Help_offers_the_user_guide_above_the_links_on_both_menus()
    {
        var (window, _, _, _) = Show();
        var classic = window.FindControl<Menu>("ClassicMenu")!.Items.OfType<MenuItem>()
            .Single(m => (string)m.Header! == "_Help");

        Assert.Equal("_User Guide", (string)classic.Items.OfType<MenuItem>().First().Header!);
        Assert.NotNull(Item(window, "_Help", "_User Guide").Command);
    }

    [AvaloniaFact]
    public async Task Choosing_the_user_guide_opens_the_extracted_file()
    {
        var opener = new FakeUriOpener();
        var vm = new SessionViewModel(new FakeEmulatorSession(), action => action(), new FakeTextClipboard(),
            uriOpener: opener);
        var window = new SessionWindow(MenuStyle.Native, isMacOS: true) { DataContext = vm };
        window.Show();

        try
        {
            ((INativeMenuItemExporterEventsImplBridge)Item(window, "_Help", "_User Guide")).RaiseClicked();
            await Wait.UntilAsync(() => opener.Opened.Count == 1, "the guide to be opened");

            Assert.EndsWith($"lizterm-user-guide-{AppVersion.Current}.html", opener.Opened[0]);
            Assert.True(File.Exists(opener.Opened[0]));
        }
        finally
        {
            if (opener.Opened.Count > 0 && File.Exists(opener.Opened[0])) File.Delete(opener.Opened[0]);
        }
    }

    [AvaloniaFact]
    public async Task A_platform_that_cannot_open_the_guide_names_the_page_on_GitHub()
    {
        var opener = new FakeUriOpener { Result = false };
        var vm = new SessionViewModel(new FakeEmulatorSession(), action => action(), new FakeTextClipboard(),
            uriOpener: opener);
        var window = new SessionWindow(MenuStyle.Native, isMacOS: true) { DataContext = vm };
        window.Show();

        ((INativeMenuItemExporterEventsImplBridge)Item(window, "_Help", "_User Guide")).RaiseClicked();
        await Wait.UntilAsync(() => vm.ErrorMessage is not null, "the banner");

        Assert.Contains(ProjectLinks.UserGuide, vm.ErrorMessage!);
    }
}
