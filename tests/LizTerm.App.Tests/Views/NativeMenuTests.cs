using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using LizTerm.App.Menus;
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
    [AvaloniaFact]
    public void The_application_menu_carries_about_and_quit()
    {
        var menu = NativeMenu.GetMenu(Application.Current!);

        Assert.NotNull(menu);
        var headers = menu!.Items.OfType<NativeMenuItem>().Select(i => i.Header!).ToArray();
        Assert.Equal(["About LizTerm", "Quit LizTerm"], headers);
    }

    /// <summary>The one gesture outside the Edit menu in this whole feature. The application menu is macOS-only
    /// by construction and Keymap claims no Meta chord, so Cmd+Q cannot swallow a key the host needed; omitting
    /// it is the riskier choice, since a replaced application menu may not inherit AppKit's own Quit item.</summary>
    [AvaloniaFact]
    public void Quit_carries_cmd_q_and_about_carries_no_gesture()
    {
        var menu = NativeMenu.GetMenu(Application.Current!)!;
        var items = menu.Items.OfType<NativeMenuItem>().ToArray();

        Assert.Null(items.Single(i => i.Header == "About LizTerm").Gesture);
        Assert.Equal(new KeyGesture(Key.Q, KeyModifiers.Meta), items.Single(i => i.Header == "Quit LizTerm").Gesture);
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
    public void The_window_menu_has_the_same_four_top_level_menus_as_the_classic_one()
    {
        var (window, _, _, _) = Show();

        var headers = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Select(i => i.Header!).ToArray();
        Assert.Equal(["_File", "_Edit", "_Keys", "_Help"], headers);
    }

    [AvaloniaFact]
    public void File_transfer_follows_the_connection_state()
    {
        var (window, _, session, _) = Show();
        var item = Item(window, "_File", "File _Transfer...");

        Assert.False(item.IsEnabled);
        session.RaiseConnection(ConnectionState.Connected3270);
        Assert.True(item.IsEnabled);
        session.RaiseConnection(ConnectionState.Disconnected);
        Assert.False(item.IsEnabled);
    }

    /// <summary>The delicate one. A wire log value corrected from inside its own change notification is
    /// invisible to a two-way binding mid-write, which is why SessionViewModel marshals its correction through
    /// dispatch; that behaviour has to survive the move to a menu the visual tree cannot see.</summary>
    [AvaloniaFact]
    public void The_wire_log_item_is_a_checkbox_bound_two_way()
    {
        var (window, vm, _, _) = Show();
        var item = Item(window, "_Help", "_Wire Log");

        Assert.Equal(MenuItemToggleType.CheckBox, item.ToggleType);
        Assert.False(item.IsChecked);

        var directory = Path.Combine(Path.GetTempPath(), "lizterm-native-" + Guid.NewGuid().ToString("N"));
        vm.WireLogDirectory = directory;
        try
        {
            vm.IsWireLogging = true;
            Assert.True(item.IsChecked);

            item.IsChecked = false;
            Assert.False(vm.IsWireLogging);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
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

    /// <summary>The mechanical parity guard the other tests above do not provide: none of them walk the full
    /// Keys or Edit submenu, so a dropped separator, a reordered item, or — the case that matters most on a
    /// 3270 client — a CommandParameter copied from the wrong line (a "PA1"-headed item silently wired to
    /// TerminalKey.PA2) would pass every test above while still sending the wrong key to the mainframe. This
    /// test walks all four top-level menus item for item, normalising across the two menu kinds' different
    /// item types (MenuItem/NativeMenuItem) and separator types (Separator/NativeMenuItemSeparator).</summary>
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
    /// <c>TerminalScreen.TryHandleClipboardKey</c> alone. That is real coverage against two regressions — a
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
}
