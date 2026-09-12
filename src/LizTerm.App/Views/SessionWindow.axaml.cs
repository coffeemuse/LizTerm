// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LizTerm.App.Files;
using LizTerm.App.Menus;
using LizTerm.App.ViewModels;
using LizTerm.Core.Settings;

namespace LizTerm.App.Views;

public partial class SessionWindow : Window
{
    /// <summary>The platform's default style and nothing else — the designer's constructor, and the tests' where
    /// the menu is not what is under test. App passes the user's style explicitly; a window that reached for
    /// App.Settings itself would open the real settings file from every headless test that builds one.</summary>
    public SessionWindow() : this(MenuStyle.Auto, OperatingSystem.IsMacOS()) { }

    /// <summary>The style and the platform are both constructor arguments rather than static reads, so a test can
    /// build a window in any mode on any machine — the once-only paste test needs all three styles, and
    /// MenuStrategy.Resolve answers InWindow for every style off macOS, so pinning the style without pinning the
    /// platform would leave a Linux runner with InWindow whatever the test asked for.
    ///
    /// The style is resolved here rather than by the caller: unresolved is not a state ApplyMenuStyle can draw,
    /// and one entry point that always resolves is what keeps a caller from having to remember. Note this
    /// platform answer governs the menu *style* only. Where About and Preferences belong is the platform the
    /// process is actually running on, which ApplyPlatformMenuRules reads directly, as its tests expect.</summary>
    internal SessionWindow(MenuStyle style, bool isMacOS)
    {
        _isMacOS = isMacOS;
        InitializeComponent();
        // The screen's events call the view model's methods, not its commands: each method carries its own guard,
        // and a keystroke must never be dropped for arriving while the previous one's round trip is still open.
        Screen.KeyRequested += (_, key) => _ = ViewModel?.SendKeyAsync(key);
        Screen.TextEntered += (_, text) => _ = ViewModel?.TypeTextAsync(text);
        Screen.CellClicked += (_, cell) => _ = ViewModel?.MoveCursorAsync(cell.Row, cell.Column);
        Screen.CopyRequested += (_, _) => _ = ViewModel?.CopyAsync();
        Screen.PasteRequested += (_, _) => _ = ViewModel?.PasteAsync();
        Screen.SelectAllRequested += (_, _) => ViewModel?.SelectAll();
        Screen.FindRequested += (_, _) => ShowFind();
        // The keypad's keys take the screen's route: the method, never the command. Focus last, a no-op while the
        // non-focusable buttons leave the keyboard alone, and the guarantee when something else (the find box) had it.
        KeypadPanel.KeyRequested += (_, key) =>
        {
            _ = ViewModel?.SendKeyAsync(key);
            Screen.Focus();
        };
        // "A pointer press ends a modifier tap" is a rule about the window, not about the keypad (keypad spec §6.2):
        // Right Ctrl held across a press anywhere in here must not become Enter on its release. The screen applies it
        // to presses on itself; this tunnelled handler applies it to every surface the screen never sees, which is
        // every surface that takes no focus — the keypad's border padding and the margins between its buttons, a
        // button the pointer leaves before releasing (neither raises Click), the status bar and the error bar.
        AddHandler(PointerPressedEvent, (_, _) => Screen.CancelTap(), RoutingStrategies.Tunnel);
        ApplyMenuStyle(MenuStrategy.Resolve(style, isMacOS));
        Opened += (_, _) =>
        {
            _opened = true;
            // The settings subscription starts here rather than the moment the data context arrives: a window
            // built and then never shown — App.OpenSession throwing before its Show() — never raises Closed
            // either, so a subscription taken earlier would leave the process-wide settings object calling
            // ApplyMenuStyle on a dead window for the life of the process. Nothing changes the style between
            // the two, so nothing is missed.
            if (_styleSource is not null) _styleSource.PropertyChanged += OnSettingsChanged;
            ShowPlatformGestures();
            Screen.Focus();
        };
    }

    private MenuStyle _menuStyle;

    /// <summary>The platform as far as the menu style is concerned. A constructor argument because Resolve
    /// collapses every style to InWindow off macOS, so a test naming a style has to name the platform too.</summary>
    private readonly bool _isMacOS;

    /// <summary>Whether Opened has run, and so whether this.GetPlatformSettings() can answer.</summary>
    private bool _opened;

    /// <summary>The declared menu's items while a style that hides the system menu bar has them removed. They are
    /// the same NativeMenuItem objects throughout: refilling adds these back to the declared instance, which is
    /// the only instance the macOS exporter will accept (#60).</summary>
    private readonly List<NativeMenuItemBase> _stashedMenuItems = [];

    /// <summary>The SettingsViewModel this window follows for live style changes, so a data-context swap can
    /// unsubscribe from the old one — the same shape as _bellSource below.</summary>
    private SettingsViewModel? _styleSource;

    /// <summary>The view model whose BellRang this window is subscribed to, so a data-context swap can unsubscribe
    /// from the old one before a bell from it flashes a screen it no longer owns.</summary>
    private SessionViewModel? _bellSource;

    /// <summary>Whether the current style exports the menu definition, which is what draws the system menu bar on
    /// macOS and installs its key equivalents.</summary>
    private bool NativeMenuExported => _menuStyle is MenuStyle.Native or MenuStyle.Both;

    /// <summary>The window's native menu while a style exports it, else null. Under InWindow the declared menu is
    /// still attached — the exporter accepts no other instance, see ApplyMenuStyle — but holds no items, and
    /// MenuLookup.Required throws for a menu that is there and lacks the item asked for; so every native lookup
    /// goes through this, where null keeps its meaning of "nothing is exported".</summary>
    private NativeMenu? ExportedMenu => NativeMenuExported ? NativeMenu.GetMenu(this) : null;

    /// <summary>One definition, two renderers, and a style naming which of them are live. Hiding the classic menu
    /// under Native is required rather than tidy: measured on macOS, a NativeMenu installed while the classic Menu
    /// was still visible drew both — an in-window bar beneath the system bar. Both is that same state, chosen on
    /// purpose rather than suppressed.
    ///
    /// Called again whenever the preference changes on an open window, so every branch has to be reversible.</summary>
    private void ApplyMenuStyle(MenuStyle style)
    {
        // Auto names no renderer and neither does a value that is not a member at all, so either would hide both
        // bars and empty the native menu — a window with no menu, and on Windows and Linux no route to
        // Preferences to undo it. MenuStrategy.Resolve is what turns both into a real style and every caller
        // goes through it, so anything arriving here unresolved is a bug in the caller, not a state to render.
        _menuStyle = style switch
        {
            MenuStyle.Native or MenuStyle.InWindow or MenuStyle.Both => style,
            _ => throw new ArgumentOutOfRangeException(
                nameof(style), style, "A window's menu style must come from MenuStrategy.Resolve."),
        };
        ClassicMenu.IsVisible = style is MenuStyle.InWindow or MenuStyle.Both;
        NativeBar.IsVisible = NativeMenuExported;

        // Hiding NativeMenuBar is not enough to turn the native path off, because NativeMenuBar is not what
        // exports the menu. A window's NativeMenu goes out through the window's own
        // ITopLevelNativeMenuExporter — NativeMenu.MenuProperty's own change handler calls SetNativeMenu on it —
        // and NativeMenuBar only *consumes* the same property to draw an in-window fallback. Left populated under
        // the classic strategy, the definition would still install AppKit key equivalents and draw a system menu
        // bar on macOS beside the in-window one, and would still be handed to a Linux global-menu registrar
        // (Plasma's Application Menu applet, Unity) while the classic bar drew it in-window too. LIZTERM_MENU
        // exists so that a native gesture swallowing a 3270 key can be turned off; it has to actually turn it
        // off.
        //
        // Emptied rather than detached (#60). This used to be NativeMenu.SetMenu(this, null), on the belief that
        // both backends treat a null menu as an empty one. They do — and that is the problem. Avalonia 12.1.2's
        // AvaloniaNativeMenuExporter (macOS) initialises its native proxy with the first NativeMenu instance a
        // window is given, and the proxy's Update throws "The menu being updated does not match" for any other
        // instance; SetNativeMenu(null) normalises null to a *fresh* NativeMenu, so the detach threw from this
        // constructor on every macOS launch with LIZTERM_MENU=classic, and the app fell to StartupErrorWindow.
        // Setting a new empty NativeMenu would throw for the same reason. The one instance the exporter will
        // accept is the declared one, so it stays attached and loses its items: Update on the same instance
        // with none removes and disposes every native item, and an NSMenu with no items installs no key
        // equivalent. Removed from the end one at a time rather than Clear(): AvaloniaList's Clear raises a
        // Reset that names no OldItems, so NativeMenu never nulls the removed items' Parent.
        //
        // Stashed rather than discarded, because the preference can be changed back: the removed items are the
        // objects the refill adds to that same declared instance, so no branch here needs to build a menu.
        var declared = NativeMenu.GetMenu(this)!;
        if (NativeMenuExported) RefillNativeMenu(declared); else EmptyNativeMenu(declared);

        ApplyPlatformMenuRules();
        // The gestures need a TopLevel's platform settings, so the first pass is the Opened handler's; a style
        // changed later re-runs it, since a refilled menu needs its key equivalents put back.
        if (_opened) ShowPlatformGestures();
    }

    private void EmptyNativeMenu(NativeMenu declared)
    {
        if (declared.Items.Count == 0) return;
        _stashedMenuItems.Clear();
        _stashedMenuItems.AddRange(declared.Items);
        while (declared.Items.Count > 0) declared.Items.RemoveAt(declared.Items.Count - 1);
    }

    private void RefillNativeMenu(NativeMenu declared)
    {
        foreach (var item in _stashedMenuItems) declared.Items.Add(item);
        _stashedMenuItems.Clear();
    }

    /// <summary>Where About and Preferences belong, in both renderers. Re-run after a refill: under InWindow
    /// ExportedMenu is null and the native half of each rule is skipped, so the items come back needing it.</summary>
    private void ApplyPlatformMenuRules()
    {
        // macOS puts About in the application menu, so neither renderer's Help item may also carry one. Both get
        // the rule: LIZTERM_MENU=classic on macOS is reachable, and there the classic bar renders in-window while
        // the application menu still supplies its own About. Under that strategy ExportedMenu answers null,
        // because the block above emptied the menu — which is the point, not an omission. MenuLookup.Required
        // answers null for that and only that: a menu that is there and does not declare the item throws.
        //
        // The separator above About goes with it, in both menus. Nothing collapses a trailing divider, so an
        // About hidden on its own leaves the Help menu ending in one.
        var aboutInHelp = MenuStrategy.AboutInHelpMenu(OperatingSystem.IsMacOS());
        AboutMenuItem.IsVisible = aboutInHelp;
        AboutSeparator.IsVisible = aboutInHelp;
        if (MenuLookup.Required(ExportedMenu, "_Help", "_About LizTerm...") is { } nativeAbout)
        {
            nativeAbout.IsVisible = aboutInHelp;
            if (MenuLookup.SeparatorAbove(nativeAbout) is { } separator) separator.IsVisible = aboutInHelp;
        }

        // macOS puts Preferences in the application menu, with Cmd-comma, so Edit carries one only elsewhere.
        // The same shape as About above: both renderers, the separator included.
        var preferencesInEdit = MenuStrategy.PreferencesInEditMenu(OperatingSystem.IsMacOS());
        PreferencesMenuItem.IsVisible = preferencesInEdit;
        PreferencesSeparator.IsVisible = preferencesInEdit;
        if (MenuLookup.Required(ExportedMenu, "_Edit", "P_references...") is { } nativePreferences)
        {
            nativePreferences.IsVisible = preferencesInEdit;
            if (MenuLookup.SeparatorAbove(nativePreferences) is { } separator) separator.IsVisible = preferencesInEdit;
        }
    }

    // MenuItem.Click is EventHandler<RoutedEventArgs> and NativeMenuItem.Click is EventHandler<EventArgs>, so
    // each shared action is two one-line handlers over one method rather than two implementations. All seven
    // live here, in the task that adds the XAML referencing them, so nothing is defined without a caller.
    private void OnCloseClickNative(object? sender, EventArgs e) => Close();

    private void OnNewSessionClickNative(object? sender, EventArgs e) =>
        (Avalonia.Application.Current as App)?.ShowPicker();

    private async void OnFileTransferClickNative(object? sender, EventArgs e) => await ShowFileTransferAsync();

    private async void OnAboutClickNative(object? sender, EventArgs e) => await ShowAboutAsync();

    private void OnPreferencesClick(object? sender, RoutedEventArgs e) => ShowPreferences();
    private void OnPreferencesClickNative(object? sender, EventArgs e) => ShowPreferences();

    private static void ShowPreferences() => (Avalonia.Application.Current as App)?.ShowPreferences();

    // Deliberately the view model's methods, never the [RelayCommand]s. Each method carries its own guard; the
    // commands keep CommunityToolkit's default of disabling while running, which is fine for a click and wrong
    // for a keystroke — and on macOS Task 6's gestures make these keystrokes, activated by the OS.
    //
    // Each one checks FindBox.IsFocused first. Task 8 gave this window its first focusable text field, and on
    // macOS that made it reachable by these same key equivalents: NSApplication.sendEvent: dispatches an AppKit
    // key equivalent to the menu ahead of the key window's responder chain, and Avalonia's TextBox is not a
    // native NSTextField — it lives inside the same Avalonia NSView as the terminal, downstream of that
    // interception. Without this guard, Cmd+V with the find box focused would fire this handler and type the
    // clipboard into the 3270 screen and send it to the host — unintended input to a live session — while the
    // box stayed empty; Cmd+A would select the whole terminal instead of the box's text; and Cmd+C would be
    // intermittent, copying whichever of the two has something to copy. Routing to the box's own
    // Copy/Paste/SelectAll instead keeps the key equivalent doing what the user looking at the focused box
    // expects. The classic (non-native) handlers and the [RelayCommand]s are untouched on purpose: that path
    // dispatches through the normal focus chain, so the box already wins there without help.
    private void OnCopyClickNative(object? sender, EventArgs e)
    {
        if (FindBox.IsFocused) { FindBox.Copy(); return; }
        _ = ViewModel?.CopyAsync();
    }

    private void OnPasteClickNative(object? sender, EventArgs e)
    {
        if (FindBox.IsFocused) { FindBox.Paste(); return; }
        _ = ViewModel?.PasteAsync();
    }

    private void OnSelectAllClickNative(object? sender, EventArgs e)
    {
        if (FindBox.IsFocused) { FindBox.SelectAll(); return; }
        ViewModel?.SelectAll();
    }

    // MenuItem.Click and NativeMenuItem.Click have different delegate shapes, so each shared action is two
    // one-line handlers over one method.
    private void OnSaveScreenClick(object? sender, RoutedEventArgs e) => _ = SaveScreenAsync();
    private void OnSaveScreenClickNative(object? sender, EventArgs e) => _ = SaveScreenAsync();

    private async Task SaveScreenAsync()
    {
        if (ViewModel is not { } vm) return;
        await vm.SaveScreenAsync(new AvaloniaFilePicker(this));
        Screen.Focus();
    }

    private void OnSaveAsProfileClick(object? sender, RoutedEventArgs e) => _ = SaveAsProfileAsync();
    private void OnSaveAsProfileClickNative(object? sender, EventArgs e) => _ = SaveAsProfileAsync();

    /// <summary>Same shape as <see cref="SaveScreenAsync"/> above, and for the same reason: this opens the profile
    /// editor as a modal dialog, so the keyboard has to come back to the screen once it closes.</summary>
    private async Task SaveAsProfileAsync()
    {
        if (ViewModel is not { } vm) return;
        await vm.SaveAsProfileAsync();
        Screen.Focus();
    }

    // Native only: the classic item binds CopyScreenAsHtmlCommand. This calls the method rather than the
    // command for the reason the Edit menu's other native items do — a command disables while it runs.
    private void OnCopyScreenClickNative(object? sender, EventArgs e) => _ = ViewModel?.CopyScreenAsHtmlAsync();

    private void OnFindClick(object? sender, RoutedEventArgs e) => ShowFind();
    private void OnFindClickNative(object? sender, EventArgs e) => ShowFind();

    /// <summary>Opens the bar and puts the caret in it. Focus is the whole point of a docked bar over a modal
    /// dialog: the screen being searched stays visible behind it.</summary>
    private void ShowFind()
    {
        if (ViewModel is not { } vm) return;
        vm.Find.Open();
        FindBox.Focus();
        FindBox.SelectAll();
    }

    private void CloseFind()
    {
        ViewModel?.Find.Close();
        Screen.Focus();
    }

    private void OnFindCloseClick(object? sender, RoutedEventArgs e) => CloseFind();
    private void OnFindNextClick(object? sender, RoutedEventArgs e) => _ = ViewModel?.Find.NextAsync();
    private void OnFindPreviousClick(object? sender, RoutedEventArgs e) => _ = ViewModel?.Find.PreviousAsync();

    /// <summary>Enter walks forward, Shift+Enter back, Escape closes. Handled on the box rather than on the
    /// window so a keystroke meant for the terminal is never taken while the bar is shut.</summary>
    private void OnFindBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        switch (e.Key)
        {
            case Key.Escape:
                CloseFind();
                e.Handled = true;
                break;
            case Key.Enter:
                _ = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? vm.Find.PreviousAsync() : vm.Find.NextAsync();
                e.Handled = true;
                break;
        }
    }

    /// <summary>The native Wire Log item needs this handler for two independent reasons, and the classic item
    /// needs it for neither — which is why it is the one place the two menus' bindings differ (OneWay here,
    /// TwoWay there).
    ///
    /// First, it is what makes the item usable at all on macOS. Avalonia's exporter gives every NSMenuItem the
    /// validation predicate <c>(Command != null || HasClickHandlers) &amp;&amp; IsEnabled</c>
    /// (<c>__MicroComIAvnMenuItemProxy.UpdateAction</c>); an item carrying only a binding satisfies neither
    /// disjunct, so AppKit greys it out and never calls back. Second, a NativeMenuItem does not toggle itself:
    /// <c>NativeMenuItem.RaiseClicked</c> raises Click and executes Command and never touches IsChecked, so
    /// even an enabled item would leave the check mark and the log alone. The view model is therefore the only
    /// thing that flips, and the OneWay binding carries the new state back to the check mark — including the
    /// correction to false that SessionViewModel.OnIsWireLoggingChanged marshals when the log will not open.
    ///
    /// The classic item is the other way round because <c>DefaultMenuInteractionHandler.Click</c> toggles a
    /// MenuItem's IsChecked *before* raising Click, and its TwoWay binding carries that to the view model. The
    /// in-window NativeMenuBar fallback runs that same handler over a MenuItem bound TwoWay to this
    /// NativeMenuItem, so it toggles too and then calls RaiseClicked: OneWay here is what stops that path
    /// toggling twice and ending where it started.</summary>
    private void OnWireLogClickNative(object? sender, EventArgs e)
    {
        if (ViewModel is { } vm) vm.IsWireLogging = !vm.IsWireLogging;
    }

    // Two one-line handlers per mode: MenuItem.Click and NativeMenuItem.Click have different delegate shapes.
    private void OnCrosshairNoneClick(object? sender, RoutedEventArgs e) => SetCrosshair(CrosshairMode.None);
    private void OnCrosshairNoneClickNative(object? sender, EventArgs e) => SetCrosshair(CrosshairMode.None);
    private void OnCrosshairHorizontalClick(object? sender, RoutedEventArgs e) => SetCrosshair(CrosshairMode.Horizontal);
    private void OnCrosshairHorizontalClickNative(object? sender, EventArgs e) => SetCrosshair(CrosshairMode.Horizontal);
    private void OnCrosshairVerticalClick(object? sender, RoutedEventArgs e) => SetCrosshair(CrosshairMode.Vertical);
    private void OnCrosshairVerticalClickNative(object? sender, EventArgs e) => SetCrosshair(CrosshairMode.Vertical);
    private void OnCrosshairBothClick(object? sender, RoutedEventArgs e) => SetCrosshair(CrosshairMode.Both);
    private void OnCrosshairBothClickNative(object? sender, EventArgs e) => SetCrosshair(CrosshairMode.Both);

    private void SetCrosshair(CrosshairMode mode)
    {
        if (ViewModel is { } vm) vm.Settings.Crosshair = mode;
    }

    private void OnKeypadClick(object? sender, RoutedEventArgs e) => ToggleKeypad();
    private void OnKeypadClickNative(object? sender, EventArgs e) => ToggleKeypad();

    /// <summary>Flips the setting; the one-way bindings carry the check mark back on both menus and the panel into
    /// or out of every window (keypad spec §6.3).</summary>
    private void ToggleKeypad()
    {
        if (ViewModel is { } vm) vm.Settings.Keypad = !vm.Settings.Keypad;
    }

    // The dock's two, the same two-handlers-per-value shape as the crosshair's above (#71).
    private void OnKeypadBottomClick(object? sender, RoutedEventArgs e) => SetKeypadDock(KeypadDock.Bottom);
    private void OnKeypadBottomClickNative(object? sender, EventArgs e) => SetKeypadDock(KeypadDock.Bottom);
    private void OnKeypadRightClick(object? sender, RoutedEventArgs e) => SetKeypadDock(KeypadDock.Right);
    private void OnKeypadRightClickNative(object? sender, EventArgs e) => SetKeypadDock(KeypadDock.Right);

    /// <summary>Writes the setting, which is where the dock lives whichever door it was changed through: the
    /// setter applies it in memory, notifies every window and the Preferences radios, and saves it to
    /// settings.json. The menu surfaces the preference; it does not hold one of its own (#71).</summary>
    private void SetKeypadDock(KeypadDock dock)
    {
        if (ViewModel is { } vm) vm.Settings.KeypadDock = dock;
    }

    /// <summary>Menu gesture text from the platform table, so macOS shows Cmd and the others show Ctrl.
    /// The native items take a real Gesture rather than display text: on macOS that is an AppKit key
    /// equivalent, dispatched by the OS before the focused screen sees the key. That is safe for exactly these
    /// four, which TerminalScreen already routes away from the host, and is why nothing on File, Keys or Help
    /// carries one.
    ///
    /// Under InWindow, ApplyMenuStyle has emptied the native menu and ExportedMenu answers null,
    /// so the four classic InputGesture assignments still run and the four native ones find no item and do
    /// nothing — no key equivalent is installed for a menu with nothing in it.</summary>
    private void ShowPlatformGestures()
    {
        var hotkeys = this.GetPlatformSettings()?.HotkeyConfiguration;
        if (hotkeys is null) return;
        CopyMenuItem.InputGesture = hotkeys.Copy.FirstOrDefault();
        PasteMenuItem.InputGesture = hotkeys.Paste.FirstOrDefault();
        SelectAllMenuItem.InputGesture = hotkeys.SelectAll.FirstOrDefault();

        // Null under the classic strategy, where the menu is emptied and there is nothing to install a key
        // equivalent on. Required draws the line the plain lookup could not: null here means only that, and a
        // menu missing one of these four items throws rather than dropping its gesture silently.
        var menu = ExportedMenu;
        Gesture(menu, "_Copy", hotkeys.Copy.FirstOrDefault());
        Gesture(menu, "_Paste", hotkeys.Paste.FirstOrDefault());
        Gesture(menu, "Select _All", hotkeys.SelectAll.FirstOrDefault());

        // Built rather than read: PlatformHotkeyConfiguration has no Find. CommandModifiers still gives Cmd on
        // macOS and Ctrl elsewhere.
        var find = new KeyGesture(Key.F, hotkeys.CommandModifiers);
        FindMenuItem.InputGesture = find;
        Gesture(menu, "_Find...", find);

        static void Gesture(NativeMenu? menu, string child, KeyGesture? gesture)
        {
            if (MenuLookup.Required(menu, "_Edit", child) is { } item) item.Gesture = gesture;
        }
    }

    private SessionViewModel? ViewModel => DataContext as SessionViewModel;

    /// <summary>Follows the data context for the two view-model events the window handles itself. Every other
    /// binding is XAML; the flash is a method call on the screen and the menu style rebuilds controls, neither of
    /// which XAML can express.</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_bellSource is not null) _bellSource.BellRang -= OnBellRang;
        _bellSource = ViewModel;
        if (_bellSource is not null) _bellSource.BellRang += OnBellRang;

        if (_styleSource is not null) _styleSource.PropertyChanged -= OnSettingsChanged;
        _styleSource = ViewModel?.Settings;
        // Only once the window is open; see the Opened handler for why.
        if (_opened && _styleSource is not null) _styleSource.PropertyChanged += OnSettingsChanged;
    }

    private void OnBellRang(object? sender, EventArgs e) => Screen.Flash();

    /// <summary>Only changes, never the value on arrival. The constructor's argument is the whole truth about
    /// the style this window opened with, and it has to outrank whatever the data context's settings say: the
    /// App tests build a window in a named style and give it a SessionViewModel whose Settings is its own
    /// in-memory instance reading Auto (SessionViewModel's constructor defaults it), so re-reading here would
    /// resolve that to the platform default and override the style under test. In the app the two always agree
    /// — App passes Resolve(Settings.MenuStyle, …) and seeds before any window exists — so nothing is lost by
    /// not re-reading. A change means the user moved the radio, and every window follows it.</summary>
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsViewModel.MenuStyle)) return;
        var style = MenuStrategy.Resolve(_styleSource!.MenuStyle, _isMacOS);
        if (style != _menuStyle) ApplyMenuStyle(style);
    }

    /// <summary>The other half of both subscriptions' lifetime (bell spec §4): a view model that outlives its
    /// window must not flash a screen that is gone, and the process-wide settings object outlives every window.</summary>
    protected override void OnClosed(EventArgs e)
    {
        if (_bellSource is not null) _bellSource.BellRang -= OnBellRang;
        _bellSource = null;
        if (_styleSource is not null) _styleSource.PropertyChanged -= OnSettingsChanged;
        _styleSource = null;
        base.OnClosed(e);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>The command clears the message; this puts the keyboard back on the screen, where the next keystroke
    /// belongs (spec 7).</summary>
    private void OnDismissClick(object? sender, RoutedEventArgs e) => Screen.Focus();

    private void OnNewSessionClick(object? sender, RoutedEventArgs e) =>
        (Avalonia.Application.Current as App)?.ShowPicker();

    private async void OnFileTransferClick(object? sender, RoutedEventArgs e) => await ShowFileTransferAsync();

    private async void OnAboutClick(object? sender, RoutedEventArgs e) => await ShowAboutAsync();

    /// <summary>The shared body. Task 5 adds the native menu's About handler over this same method — the two
    /// menus' Click events have different delegate shapes, so neither can reuse the other's handler.</summary>
    private async Task ShowAboutAsync()
    {
        if (ViewModel is not { } vm) return;
        try
        {
            if (Avalonia.Application.Current is App app) await app.ShowAboutAsync(this);
        }
        catch (Exception ex)
        {
            vm.ErrorMessage = "Could not open About: " + ex.Message;
        }
        Screen.Focus();
    }

    /// <summary>Opens the File Transfer dialog modally over this window. The dialog's own picker parents the OS
    /// file dialogs; the view model comes from the session view model so the last request is remembered.</summary>
    private async Task ShowFileTransferAsync()
    {
        if (ViewModel is not { IsConnected: true } vm) return;
        try
        {
            var dialog = new FileTransferWindow();
            dialog.DataContext = vm.CreateTransfer(new AvaloniaFilePicker(dialog));
            await dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            vm.ErrorMessage = "Could not open the IND$FILE Transfer dialog: " + ex.Message;
        }
        Screen.Focus();
    }
}
