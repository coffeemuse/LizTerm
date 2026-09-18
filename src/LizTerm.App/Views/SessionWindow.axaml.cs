// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using LizTerm.App.Dialogs;
using LizTerm.App.Files;
using LizTerm.App.HostFiles;
using LizTerm.App.Menus;
using LizTerm.App.Sessions;
using LizTerm.App.ViewModels;
using LizTerm.Core.Settings;

namespace LizTerm.App.Views;

public partial class SessionWindow : Window, ISessionHost
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
        _nativeWindowMenu = MenuLookup.Item(NativeMenu.GetMenu(this), "_Window")!.Menu!;
        _nativeMinimize = MenuLookup.Item(_nativeWindowMenu, "_Minimize")!;
        _nativeZoom = MenuLookup.Item(_nativeWindowMenu, "_Zoom")!;
        _nativeMinimizeSeparator = _nativeWindowMenu.Items.OfType<NativeMenuItemSeparator>().First();
        _nativeKeepOnTop = MenuLookup.Item(_nativeWindowMenu, "_Keep on Top")!;
        _nativeSessionsSeparator = _nativeWindowMenu.Items.OfType<NativeMenuItemSeparator>().Last();
        _nativeMvsmfBrowser = MenuLookup.Item(NativeMenu.GetMenu(this), "_File", "mvsMF _Browser...")!;
        RebuildSessionRows();
        // The screen's events call the view model's methods, not its commands: each method carries its own guard,
        // and a keystroke must never be dropped for arriving while the previous one's round trip is still open.
        Screen.KeyRequested += (_, key) => _ = ViewModel?.SendKeyAsync(key);
        Screen.TextEntered += (_, text) => _ = ViewModel?.TypeTextAsync(text);
        Screen.CellClicked += (_, cell) => _ = ViewModel?.MoveCursorAsync(cell.Row, cell.Column);
        Screen.CopyRequested += (_, _) => _ = ViewModel?.CopyAsync();
        Screen.PasteRequested += (_, _) => _ = ViewModel?.PasteAsync();
        Screen.SelectAllRequested += (_, _) => ViewModel?.SelectAll();
        Screen.FindRequested += (_, _) => ShowFind();
        Screen.SwitcherRequested += (_, _) => ToggleSwitcher();
        SwitcherPanel.Chosen += (_, entry) => ChooseSession(entry);
        SwitcherPanel.Dismissed += (_, _) => CloseSwitcher();
        // A switcher left open over a window the user has moved away from would greet them with a stale list.
        Deactivated += (_, _) =>
        {
            if (Switcher is { IsOpen: true }) CloseSwitcher();
        };
        // Focus arriving anywhere else in the window closes the switcher, because the screen must never hold the
        // keyboard under it (session switching spec §5.2, §5.5): the native Find item, a keypad click, a dialog handing
        // focus back. handledEventsToo, since what matters is where the keyboard went, not who marked the event.
        AddHandler(GotFocusEvent, (_, e) => OnFocusMoved(e.Source), RoutingStrategies.Bubble, handledEventsToo: true);
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

        // handledEventsToo: the item's Command marks Click handled before any instance handler runs. See OnInsertClick.
        InsertMenuItem.AddHandler(MenuItem.ClickEvent, OnInsertClick, RoutingStrategies.Bubble, handledEventsToo: true);
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
            // Subscribed here for the reason the settings are: a window never shown never raises Closed.
            if (_sessions is not null)
            {
                _sessions.Changed += OnSessionsChanged;
                RebuildSessionRows();
            }
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

    /// <summary>The native Window menu's own NativeMenu and the items the code changes, held by reference because
    /// under InWindow the top-level items are stashed out of the window's menu (MenuLookup cannot reach them) and
    /// must still be kept current for when they come back. NativeMenuItem takes no x:Name, so they are found by
    /// header once, in the constructor.</summary>
    private readonly NativeMenu _nativeWindowMenu;
    private readonly NativeMenuItem _nativeMinimize;
    private readonly NativeMenuItem _nativeZoom;
    private readonly NativeMenuItemSeparator _nativeMinimizeSeparator;
    private readonly NativeMenuItem _nativeKeepOnTop;
    private readonly NativeMenuItemSeparator _nativeSessionsSeparator;

    /// <summary>Held, like Minimize, so AttachHostFiles can show it whether or not the native menu is exported.</summary>
    private readonly NativeMenuItem _nativeMvsmfBrowser;
    private HostFileAccess? _hostFiles;

    /// <summary>The generated session rows, both renderers' items with the session each stands for.</summary>
    private readonly List<(NativeMenuItem Native, MenuItem Classic, SessionEntry Entry)> _sessionRows = [];

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

    /// <summary>Where About and Preferences belong in the exported menu, and the chord the in-window Preferences
    /// names. Re-run after a refill: under InWindow ExportedMenu is null and the native half of each rule is
    /// skipped, so the items come back needing it.</summary>
    private void ApplyPlatformMenuRules()
    {
        // macOS puts About in the application menu, so the exported Help menu must not carry a second one. Under
        // InWindow ExportedMenu answers null, because ApplyMenuStyle emptied the menu — which is the point, not an
        // omission. MenuLookup.Required answers null for that and only that: a menu that is there and does not
        // declare the item throws.
        //
        // The separator above About goes with it. Nothing collapses a trailing divider in NSMenu, so an About
        // hidden on its own leaves the Help menu ending in one.
        //
        // The in-window menu takes no part in this and carries both items in every style (#103). A user who picks
        // it has stopped looking at the system menu bar, which is where the application menu is; with Preferences
        // hidden in the window as well, the setting that took the bar away left no visible way back to itself.
        var aboutInHelp = MenuStrategy.AboutInExportedHelpMenu(OperatingSystem.IsMacOS());
        if (MenuLookup.Required(ExportedMenu, "_Help", "_About LizTerm...") is { } nativeAbout)
        {
            nativeAbout.IsVisible = aboutInHelp;
            if (MenuLookup.SeparatorAbove(nativeAbout) is { } separator) separator.IsVisible = aboutInHelp;
        }

        // The same shape for Preferences, which the application menu carries with Cmd-comma.
        var preferencesInEdit = MenuStrategy.PreferencesInExportedEditMenu(OperatingSystem.IsMacOS());
        if (MenuLookup.Required(ExportedMenu, "_Edit", "P_references...") is { } nativePreferences)
        {
            nativePreferences.IsVisible = preferencesInEdit;
            if (MenuLookup.SeparatorAbove(nativePreferences) is { } separator) separator.IsVisible = preferencesInEdit;
        }

        // The in-window Preferences names that chord as a label. InputGesture is display-only, so the application
        // menu's key equivalent is still what acts on it, under every style. Off macOS no chord opens Preferences.
        PreferencesMenuItem.InputGesture = MenuStrategy.PreferencesGesture(OperatingSystem.IsMacOS());

        // Minimize and Zoom are macOS's (session switching spec §7.4); elsewhere the title bar has both. Unlike About
        // and Preferences above this reads _isMacOS, the constructor's platform, so a test can build either shape on
        // any machine. Set on the held references, so it holds for stashed native items too.
        foreach (var item in new NativeMenuItem[] { _nativeMinimize, _nativeZoom, _nativeMinimizeSeparator }) item.IsVisible = _isMacOS;
        MinimizeMenuItem.IsVisible = _isMacOS;
        ZoomMenuItem.IsVisible = _isMacOS;
        MinimizeSeparator.IsVisible = _isMacOS;
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

    private async void OnCheckForUpdatesClickNative(object? sender, EventArgs e) => await CheckForUpdatesAsync();
    private async void OnCheckForUpdatesClick(object? sender, RoutedEventArgs e) => await CheckForUpdatesAsync();

    private async Task CheckForUpdatesAsync()
    {
        if (ViewModel is not { } vm) return;
        try
        {
            if (Avalonia.Application.Current is App app) await app.CheckForUpdatesManuallyAsync(this);
        }
        catch (Exception ex)
        {
            vm.ErrorMessage = "Could not check for updates: " + ex.Message;
        }
        Screen.Focus();
    }

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
    // dispatches through the normal focus chain, so the box already wins there without help. The session switcher's
    // box (#46) is the second such field, guarded the same way.
    private void OnCopyClickNative(object? sender, EventArgs e)
    {
        if (FindBox.IsFocused) { FindBox.Copy(); return; }
        if (SwitcherPanel.Box.IsFocused) { SwitcherPanel.Box.Copy(); return; }
        _ = ViewModel?.CopyAsync();
    }

    private void OnPasteClickNative(object? sender, EventArgs e)
    {
        if (FindBox.IsFocused) { FindBox.Paste(); return; }
        if (SwitcherPanel.Box.IsFocused) { SwitcherPanel.Box.Paste(); return; }
        _ = ViewModel?.PasteAsync();
    }

    private void OnSelectAllClickNative(object? sender, EventArgs e)
    {
        if (FindBox.IsFocused) { FindBox.SelectAll(); return; }
        if (SwitcherPanel.Box.IsFocused) { SwitcherPanel.Box.SelectAll(); return; }
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

    /// <summary>The classic item, Click-driven for the same reason the native one is: starting a log asks first
    /// (#139), so the request has to reach ToggleWireLogCommand rather than writing IsWireLogging through a
    /// two-way binding. Both menus are now the same shape — ToggleType, a OneWay check mark and a Click handler —
    /// which is also what keeps NativeMenuTests' command-parity guard happy, a null Command on both sides being
    /// what a Click-driven item looks like.
    ///
    /// DefaultMenuInteractionHandler.Click still ticks this item's own IsChecked before the handler runs. On a
    /// start or a stop the view model's own change puts it right; on a declined warning nothing would change, so
    /// ToggleWireLogAsync re-notifies IsWireLogging to push the binding's value back over it.</summary>
    private void OnWireLogClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm) _ = vm.ToggleWireLogCommand.ExecuteAsync(null);
    }

    /// <summary>The native item goes through the command for the confirmation, as the classic one does, and needs
    /// a handler for two further reasons of its own.
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
    /// OneWay is required here rather than tidy: the in-window NativeMenuBar fallback runs
    /// <c>DefaultMenuInteractionHandler.Click</c> over a MenuItem bound TwoWay to this NativeMenuItem, so that
    /// path toggles the native item as well and then calls RaiseClicked. A TwoWay binding to the view model
    /// would make that two toggles, ending where it started.
    ///
    /// Fire and forget, as a menu click must be.</summary>
    private void OnWireLogClickNative(object? sender, EventArgs e)
    {
        if (ViewModel is { } vm) _ = vm.ToggleWireLogCommand.ExecuteAsync(null);
    }

    /// <summary>Keys > Insert's check mark is the host's insert mode (#111), not the menu's. Both renderers write
    /// IsChecked before the click arrives (DefaultMenuInteractionHandler.Click on the classic item, the in-window
    /// NativeMenuBar fallback through its two-way binding to the native one), and the host answers the key later, if
    /// at all. So the click puts the mark back to what the host last reported; the Command still sends the key, and
    /// the one-way binding moves the mark when the host's answer arrives. Unlike Wire Log, nothing here can flip the
    /// state itself. The classic handler is added in the constructor with handledEventsToo, not by a Click attribute:
    /// a MenuItem runs its Command from its own class handler and marks Click handled first, so an attribute handler
    /// on an item that also has a Command never runs.</summary>
    private void OnInsertClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && ViewModel is { } vm) item.IsChecked = vm.IsInsertMode;
    }

    private void OnInsertClickNative(object? sender, EventArgs e)
    {
        if (sender is NativeMenuItem item && ViewModel is { } vm) item.IsChecked = vm.IsInsertMode;
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
    /// equivalent, dispatched by the OS before the focused screen sees the key. That is safe for Copy, Paste,
    /// Select All, Find and Switch Session, which TerminalScreen already routes away from the host, and for
    /// Minimize, which nothing else dispatches — why nothing on File, View, Keys or Help carries one, and
    /// Window only its two Cmd chords.
    ///
    /// Under InWindow, ApplyMenuStyle has emptied the native menu and ExportedMenu answers null, so the five
    /// classic InputGesture assignments still run and all six native ones — the sixth, Minimize, only on
    /// macOS — find no item and do nothing: no key equivalent is installed for a menu with nothing in it.</summary>
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

        // The session switcher's chord (spec §6), Find's arrangement: a real key equivalent on the native item, display
        // text on the classic one, and TerminalScreen.SwitcherRequested wherever no key equivalent is installed. A Cmd
        // chord cannot be a 3270 keystroke, which is why this and Minimize are the two exceptions outside Edit.
        var switcher = new KeyGesture(Key.K, hotkeys.CommandModifiers);
        SwitchSessionMenuItem.InputGesture = switcher;
        if (MenuLookup.Required(menu, "_Window", "_Switch Session...") is { } nativeSwitch) nativeSwitch.Gesture = switcher;
        // Cmd+M on macOS, native only: nothing else dispatches it, so the in-window item names no chord it cannot keep.
        if (_isMacOS && MenuLookup.Required(menu, "_Window", "_Minimize") is { } nativeMinimize)
            nativeMinimize.Gesture = new KeyGesture(Key.M, KeyModifiers.Meta);

        static void Gesture(NativeMenu? menu, string child, KeyGesture? gesture)
        {
            if (MenuLookup.Required(menu, "_Edit", child) is { } item) item.Gesture = gesture;
        }
    }

    private SessionViewModel? ViewModel => DataContext as SessionViewModel;

    private SessionList? _sessions;
    private SessionEntry? _ownEntry;

    /// <summary>This window's switcher, once App has attached the session list; null for a window built without
    /// one, as most tests build it.</summary>
    internal SessionSwitcherViewModel? Switcher { get; private set; }

    /// <summary>Joins this window to the process's session list (session switching spec §7.2, §9). Called by App
    /// once, before Show(). Activation is reported here so the list's use order follows the user; removal is in
    /// OnClosed, ahead of the Closed event App's shutdown test reads the count from.</summary>
    internal void AttachSessions(SessionList sessions, SessionEntry own)
    {
        if (_sessions is not null) throw new InvalidOperationException("This window already has a session list.");
        _sessions = sessions;
        _ownEntry = own;
        Switcher = new SessionSwitcherViewModel(sessions, own);
        SwitcherPanel.DataContext = Switcher;
        RebuildSessionRows();
        // Attached after Opened (a test can do this), the Opened handler has already run.
        if (_opened) sessions.Changed += OnSessionsChanged;
        Activated += (_, _) => sessions.Activated(own);
    }

    /// <summary>Cmd/Ctrl+K from the screen, and Window &gt; Switch Session... (Task 7).</summary>
    private void ToggleSwitcher()
    {
        if (Switcher is not { } switcher) return;
        if (switcher.IsOpen)
        {
            CloseSwitcher();
            return;
        }
        switcher.Open();
        SwitcherPanel.IsVisible = true;
        SwitcherPanel.FocusBox();
    }

    /// <summary>Always hands the keyboard back to the screen, as Find's Escape does.</summary>
    private void CloseSwitcher()
    {
        Switcher?.Close();
        SwitcherPanel.IsVisible = false;
        Screen.Focus();
    }

    /// <summary>The close for focus that has already moved somewhere on purpose, so unlike CloseSwitcher it leaves
    /// the keyboard where it is: Find has just put it in FindBox, and refocusing the screen would take it away, so
    /// the next letter typed would go to the host; the keypad refocuses the screen itself.</summary>
    private void CloseSwitcherLeavingFocus()
    {
        Switcher?.Close();
        SwitcherPanel.IsVisible = false;
    }

    /// <summary>Every focus arrival in the window, rather than the panel's IsKeyboardFocusWithin going false, for two
    /// reasons measured headless. The box's own context menu takes focus into a popup outside the panel, which that
    /// property counts as leaving, and right-clicking the filter to paste must not close the switcher. And the
    /// property changes only once, so focus that went nowhere first (another window shown) and later landed on the
    /// screen would leave the switcher open over it. The test is logical ancestry for that same popup: wherever it
    /// is hosted, its logical ancestors lead back to the switcher. Neither explicit close comes back through here:
    /// CloseSwitcher sets IsOpen false before it refocuses the screen, and opening focuses the box, which is the
    /// switcher's own.</summary>
    private void OnFocusMoved(object? source)
    {
        if (Switcher is not { IsOpen: true }) return;
        if (source is ILogical element && (ReferenceEquals(element, SwitcherPanel) || SwitcherPanel.IsLogicalAncestorOf(element))) return;
        CloseSwitcherLeavingFocus();
    }

    /// <summary>Closes first: bringing another window deactivates this one, whose handler would otherwise close a
    /// switcher that is mid-choice.</summary>
    private void ChooseSession(SessionEntry entry)
    {
        CloseSwitcher();
        entry.Host.Bring();
    }

    private void OnSessionsChanged(object? sender, EventArgs e) => RebuildSessionRows();

    /// <summary>One row per open session after the sessions separator, in both menus from one list (spec §7.2).
    /// Removed from the end one at a time and added to the same NativeMenu instance: never Clear(), never a new
    /// menu (#60).</summary>
    private void RebuildSessionRows()
    {
        while (_nativeWindowMenu.Items.Count > 0 && !ReferenceEquals(_nativeWindowMenu.Items[^1], _nativeSessionsSeparator))
            _nativeWindowMenu.Items.RemoveAt(_nativeWindowMenu.Items.Count - 1);
        while (WindowMenuItem.Items.Count > 0 && !ReferenceEquals(WindowMenuItem.Items[^1], SessionsSeparator))
            WindowMenuItem.Items.RemoveAt(WindowMenuItem.Items.Count - 1);
        _sessionRows.Clear();

        if (_sessions is { } sessions)
        {
            foreach (var entry in sessions.Entries)
            {
                var native = SessionMenuItems.Native(sessions, entry, BringFromMenu);
                var classic = SessionMenuItems.Classic(sessions, entry, BringFromMenu);
                _nativeWindowMenu.Items.Add(native);
                WindowMenuItem.Items.Add(classic);
                _sessionRows.Add((native, classic, entry));
            }
        }

        // Nothing collapses a trailing separator, so it goes when there is nothing under it.
        _nativeSessionsSeparator.IsVisible = _sessionRows.Count > 0;
        SessionsSeparator.IsVisible = _sessionRows.Count > 0;
    }

    /// <summary>Brings the session, then puts every mark back: both renderers write IsChecked before the click
    /// arrives, and bringing the session that is already current raises no Changed to rebuild them (Keys &gt;
    /// Insert's pattern).</summary>
    private void BringFromMenu(SessionEntry entry)
    {
        entry.Host.Bring();
        var current = _sessions?.Current;
        foreach (var (native, classic, rowEntry) in _sessionRows)
        {
            native.IsChecked = ReferenceEquals(rowEntry, current);
            classic.IsChecked = ReferenceEquals(rowEntry, current);
        }
    }

    // Two one-line handlers per action: MenuItem.Click and NativeMenuItem.Click have different delegate shapes.
    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMinimizeClickNative(object? sender, EventArgs e) => WindowState = WindowState.Minimized;
    private void OnZoomClick(object? sender, RoutedEventArgs e) => ToggleZoom();
    private void OnZoomClickNative(object? sender, EventArgs e) => ToggleZoom();
    private void OnKeepOnTopClick(object? sender, RoutedEventArgs e) => ToggleKeepOnTop();
    private void OnKeepOnTopClickNative(object? sender, EventArgs e) => ToggleKeepOnTop();
    private void OnSwitchSessionClick(object? sender, RoutedEventArgs e) => ToggleSwitcher();
    private void OnSwitchSessionClickNative(object? sender, EventArgs e) => ToggleSwitcher();
    private void OnBringAllToFrontClick(object? sender, RoutedEventArgs e) => _sessions?.BringAllToFront();
    private void OnBringAllToFrontClickNative(object? sender, EventArgs e) => _sessions?.BringAllToFront();

    /// <summary>macOS's Zoom: between maximised and normal.</summary>
    private void ToggleZoom() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>Per window and never saved (spec §7.3). The marks are put back after the flip because both renderers
    /// toggle them before the click arrives; OnPropertyChanged keeps them in step with Topmost from anywhere else.</summary>
    private void ToggleKeepOnTop()
    {
        Topmost = !Topmost;
        ShowKeepOnTopMarks();
    }

    private void ShowKeepOnTopMarks()
    {
        KeepOnTopMenuItem.IsChecked = Topmost;
        _nativeKeepOnTop.IsChecked = Topmost;
    }

    /// <summary>What the window was before it was last minimised, so Bring puts a maximised or zoomed window back
    /// that way: Normal is not "restore" on every backend (X11 clears the maximised atoms for it).</summary>
    private WindowState _stateBeforeMinimize = WindowState.Normal;

    /// <summary>ISessionHost: restore a minimised window to the state it had, then activate it.</summary>
    public void Bring()
    {
        if (WindowState == WindowState.Minimized) WindowState = _stateBeforeMinimize;
        Activate();
    }

    public bool IsMinimized => WindowState == WindowState.Minimized;

    /// <summary>Keep on Top is the window's Topmost (spec §7.3).</summary>
    public bool KeepOnTop => Topmost;

    public event EventHandler? KeepOnTopChanged;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty
            && change.GetNewValue<WindowState>() == WindowState.Minimized
            && change.GetOldValue<WindowState>() is var before and not WindowState.Minimized)
            _stateBeforeMinimize = before;
        if (change.Property != TopmostProperty) return;
        ShowKeepOnTopMarks();
        KeepOnTopChanged?.Invoke(this, EventArgs.Empty);
    }

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
        // Before base.OnClosed raises Closed: App's handler asks ShutdownPolicy with the count of sessions left.
        if (Switcher is { IsOpen: true }) Switcher.Close();
        if (_sessions is not null && _ownEntry is not null)
        {
            _sessions.Changed -= OnSessionsChanged;
            _sessions.Remove(_ownEntry);
        }
        // The browser is owned and would close with the window anyway; closing it here first means its connection
        // is released before the sign-in it uses is ended.
        MvsmfBrowser?.Close();
        // Best effort: SignOutAsync drops the token on this thread and ends the session on the pool, so the close
        // waits for nothing and the shutdown that follows the last window cannot strand the DELETE.
        if (_hostFiles is { } hostFiles)
            _ = hostFiles.SignOutAsync().WaitAsync(TimeSpan.FromSeconds(5)).ContinueWith(_ => { }, TaskScheduler.Default);
        base.OnClosed(e);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>The command clears the message; this puts the keyboard back on the screen, where the next keystroke
    /// belongs (spec 7).</summary>
    private void OnDismissClick(object? sender, RoutedEventArgs e) => Screen.Focus();

    /// <summary>The status bar's note icon (#93). A Click handler, the window's shape for the error bar's Dismiss
    /// and the find bar's buttons; the screen keeps the keyboard, so the next keystroke can take the banner
    /// away again.</summary>
    private void OnNoteIconClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ShowBanner();
        Screen.Focus();
    }

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

    /// <summary>The open mvsMF Browser, if any; one per session window.</summary>
    internal MvsmfBrowserWindow? MvsmfBrowser { get; private set; }

    /// <summary>App calls this for a profile with a REST URL (spec §3.3): the item appears in both menus and the
    /// window holds the session's sign-in until it closes.</summary>
    internal void AttachHostFiles(HostFileAccess access)
    {
        _hostFiles = access;
        _nativeMvsmfBrowser.IsVisible = true;
        MvsmfBrowserMenuItem.IsVisible = true;
    }

    private void OnMvsmfBrowserClick(object? sender, RoutedEventArgs e) => ShowMvsmfBrowser();
    private void OnMvsmfBrowserClickNative(object? sender, EventArgs e) => ShowMvsmfBrowser();

    /// <summary>Fronts the open browser, or opens one owned by this window without blocking it. The 3270
    /// connection is not needed. The prompts and pickers belong to the browser, so they open over it.</summary>
    private void ShowMvsmfBrowser()
    {
        if (MvsmfBrowser is { } open)
        {
            open.Activate();
            return;
        }
        if (_hostFiles is not { } access || ViewModel is not { } vm) return;
        if (access.Url is null)
        {
            vm.ErrorMessage = $"The profile's mvsMF URL cannot be used: {access.UrlError}";
            return;
        }
        HostFileConnection? connection = null;
        try
        {
            var browser = new MvsmfBrowserWindow();
            connection = access.Connect(new AvaloniaCredentialPrompt(browser), new AvaloniaCertificatePrompt(browser));
            browser.DataContext = vm.CreateMvsmfBrowser(access, connection, new AvaloniaFilePicker(browser));
            browser.Closed += (_, _) =>
            {
                if (ReferenceEquals(MvsmfBrowser, browser)) MvsmfBrowser = null;
            };
            MvsmfBrowser = browser;
            browser.ShowAbove(this);
        }
        catch (Exception ex)
        {
            connection?.Dispose();
            MvsmfBrowser = null;
            vm.ErrorMessage = "Could not open the mvsMF Browser: " + ex.Message;
        }
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
            await dialog.ShowDialogAbove(this);
        }
        catch (Exception ex)
        {
            vm.ErrorMessage = "Could not open the IND$FILE Transfer dialog: " + ex.Message;
        }
        Screen.Focus();
    }
}
