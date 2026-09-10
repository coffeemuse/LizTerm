// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using LizTerm.App.Controls;
using LizTerm.App.Menus;
using LizTerm.App.Rendering;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

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
    public void The_application_menu_declares_about_and_no_quit_of_its_own()
    {
        var menu = NativeMenu.GetMenu(Application.Current!);

        Assert.NotNull(menu);
        var headers = menu!.Items.OfType<NativeMenuItem>().Select(i => i.Header!).ToArray();
        Assert.Equal(["About LizTerm"], headers);
    }

    /// <summary>Not one gesture outside the Edit menu, the application menu included. Cmd+Q was ours until the
    /// measurement recorded above; AppKit's own Quit item carries it now, and a second declaration of the same
    /// chord would be a second key equivalent for it.</summary>
    [AvaloniaFact]
    public void The_application_menu_carries_no_gesture()
    {
        var menu = NativeMenu.GetMenu(Application.Current!)!;

        Assert.All(menu.Items.OfType<NativeMenuItem>(), item => Assert.Null(item.Gesture));
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
    /// that does not parse as a CrosshairMode, so a mistyped ConverterParameter in SessionWindow.axaml
    /// (say "Horizantal" for the Horizontal item) would surface here as a thrown exception on that item's own
    /// click, rather than as a check mark that silently never lights — the failure mode a test that only ever
    /// clicks Vertical could never see.</summary>
    [AvaloniaFact]
    public void Choosing_a_crosshair_mode_checks_exactly_that_item()
    {
        var (window, vm, _, _) = Show();
        var modes = new[] { CrosshairMode.None, CrosshairMode.Horizontal, CrosshairMode.Vertical, CrosshairMode.Both };
        var items = new[] { "_None", "_Horizontal", "_Vertical", "_Both" }
            .Select(header => Item(window, "_View", header)).ToArray();

        for (var chosen = 0; chosen < modes.Length; chosen++)
        {
            ((INativeMenuItemExporterEventsImplBridge)items[chosen]).RaiseClicked();

            Assert.Equal(modes[chosen], vm.Crosshair);
            Assert.Equal(
                Enumerable.Range(0, modes.Length).Select(i => i == chosen),
                items.Select(i => i.IsChecked));
        }
    }

    [AvaloniaFact]
    public void The_crosshair_reaches_the_terminal_screen()
    {
        var (window, vm, _, _) = Show();

        vm.Crosshair = CrosshairMode.Both;

        Assert.Equal(CrosshairMode.Both, window.FindControl<TerminalScreen>("Screen")!.Crosshair);
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

        // NativeMenuItemSeparator derives from NativeMenuItem, so OfType alone would demand a Click handler
        // on the dividers too.
        foreach (var top in NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>())
        {
            foreach (var item in top.Menu!.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator))
            {
                Assert.True(item.Command is not null || item.HasClickHandlers,
                    $"{top.Header} > {item.Header}: no Command and no Click handler, so macOS greys it out");
            }
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
    /// only — the classic strategy detached the menu — and a menu that is there without the item is a typo the
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
    /// case where a native gesture is swallowing a 3270 key, so it has to detach the definition, not just hide a
    /// control. The attached property is the assertion because it is the input to every exporter; headless
    /// offers no ITopLevelNativeMenuExporter, so NativeMenu.GetIsNativeMenuExported is false either way here and
    /// would prove nothing.</summary>
    [AvaloniaFact]
    public void The_classic_strategy_detaches_the_window_native_menu()
    {
        var (native, _, _, _) = Show(useNativeMenu: true);
        Assert.NotNull(NativeMenu.GetMenu(native));

        var (classic, _, _, _) = Show(useNativeMenu: false);
        Assert.Null(NativeMenu.GetMenu(classic));
    }

    /// <summary>The detach must not cost the classic menu its shortcut hints: ShowPlatformGestures sets the
    /// three classic InputGestures and then looks the three native Edit items up through NativeMenu.GetMenu,
    /// which is null under this strategy. Every native lookup finding nothing has to be a no-op, not a throw.</summary>
    [AvaloniaFact]
    public void The_classic_menu_keeps_its_gestures_when_the_native_menu_is_detached()
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
    /// top-level menus item for item, comparing header, separator position, Command and (on Keys)
    /// CommandParameter, normalising across the two menu kinds' different item types (MenuItem/NativeMenuItem)
    /// and separator types (Separator/NativeMenuItemSeparator).</summary>
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

            var classicChildren = classicTop[i].Items.Cast<object>().ToArray();
            var nativeChildren = nativeTop[i].Menu!.Items.ToArray();
            Assert.True(classicChildren.Length == nativeChildren.Length,
                $"{topHeader}: {classicChildren.Length} classic items vs {nativeChildren.Length} native items");

            for (var j = 0; j < classicChildren.Length; j++)
            {
                var classicIsSeparator = classicChildren[j] is Separator;
                var nativeIsSeparator = nativeChildren[j] is NativeMenuItemSeparator;
                Assert.True(classicIsSeparator == nativeIsSeparator,
                    $"{topHeader}[{j}]: separator position differs (classic {classicIsSeparator}, native {nativeIsSeparator})");
                if (classicIsSeparator) continue;

                var classicHeader = ((MenuItem)classicChildren[j]).Header as string;
                var nativeHeader = ((NativeMenuItem)nativeChildren[j]).Header;
                Assert.Equal(classicHeader, nativeHeader);
            }

            // Command, for every menu but Edit. Everything checked above — headers, separator positions, and
            // the Keys CommandParameter walk below — passes with "_Connect" bound to DisconnectCommand; only
            // this comparison sees it. Reference equality is the right test: both sides bind the same
            // [RelayCommand] instance off the one view model, and a null Command on both is what a Click-driven
            // item (New Session, File Transfer, Close, About) correctly looks like.
            if (topHeader != "_Edit")
            {
                for (var j = 0; j < classicChildren.Length; j++)
                {
                    if (classicChildren[j] is Separator) continue;
                    var classicItem = (MenuItem)classicChildren[j];
                    var nativeItem = (NativeMenuItem)nativeChildren[j];
                    Assert.True(ReferenceEquals(classicItem.Command, nativeItem.Command),
                        $"{topHeader} > {classicItem.Header}: classic and native bind different commands");
                }
            }

            if (topHeader == "_Edit")
            {
                // Deliberate exception, not an oversight: the classic Edit items bind Command (so the
                // [RelayCommand]s disable while running, which is correct for a mouse click on a menu item),
                // while the native Edit items use Click handlers instead, wired straight to the view model's
                // methods (a native menu gesture is a keystroke, and it must never be swallowed by a command
                // that is disabled mid-round-trip — see OnCopyClickNative and its neighbours in
                // SessionWindow.axaml.cs). Command/CommandParameter is therefore never compared for Edit; only
                // header text and separator positions are, in the loop above.
            }
            else if (topHeader == "_Keys")
            {
                // The highest-value assertion in this test. Every Keys item binds SendKeyCommand, so header
                // text alone cannot tell "PA1" wired to TerminalKey.PA1 apart from "PA1" wired to
                // TerminalKey.PA2 — only the CommandParameter can, and getting it wrong sends the wrong key to
                // the mainframe.
                for (var j = 0; j < classicChildren.Length; j++)
                {
                    if (classicChildren[j] is Separator) continue;
                    var classicParam = ((MenuItem)classicChildren[j]).CommandParameter;
                    var nativeParam = ((NativeMenuItem)nativeChildren[j]).CommandParameter;
                    Assert.Equal(classicParam, nativeParam);
                }
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

        // A gesture here would be a 3270 client that cannot send the key, silently, with nothing in the wire log.
        foreach (var (top, child) in new[]
                 {
                     ("_File", "_Connect"), ("_File", "C_lose"),
                     ("_Keys", "PA1"), ("_Keys", "PF13"), ("_Keys", "Clear"),
                     ("_Help", "_Wire Log"),
                 })
        {
            Assert.Null(Item(window, top, child).Gesture);
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

    /// <summary>Matches reach the control, so the overlay has something to paint.</summary>
    [AvaloniaFact]
    public void Find_matches_reach_the_terminal_screen()
    {
        var (window, vm, _, _) = Show();
        vm.Find.Open();

        vm.Find.Term = "hello";

        var screen = window.FindControl<TerminalScreen>("Screen")!;
        Assert.Equal(vm.Find.Matches, screen.FindMatches);
        Assert.Equal(vm.Find.CurrentMatch, screen.CurrentMatch);
    }
}
