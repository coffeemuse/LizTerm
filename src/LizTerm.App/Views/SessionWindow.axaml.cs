using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LizTerm.App.Files;
using LizTerm.App.Menus;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Views;

public partial class SessionWindow : Window
{
    public SessionWindow() : this(MenuStrategy.UseNativeMenu) { }

    /// <summary>The strategy is a constructor argument rather than a static read, so a test can build a window
    /// in either mode without touching the process environment — the once-only paste test needs both.</summary>
    internal SessionWindow(bool useNativeMenu)
    {
        InitializeComponent();
        // The screen's events call the view model's methods, not its commands: each method carries its own guard,
        // and a keystroke must never be dropped for arriving while the previous one's round trip is still open.
        Screen.KeyRequested += (_, key) => _ = ViewModel?.SendKeyAsync(key);
        Screen.TextEntered += (_, text) => _ = ViewModel?.TypeTextAsync(text);
        Screen.CellClicked += (_, cell) => _ = ViewModel?.MoveCursorAsync(cell.Row, cell.Column);
        Screen.CopyRequested += (_, _) => _ = ViewModel?.CopyAsync();
        Screen.PasteRequested += (_, _) => _ = ViewModel?.PasteAsync();
        Screen.SelectAllRequested += (_, _) => ViewModel?.SelectAll();
        ApplyMenuStrategy(useNativeMenu);
        Opened += (_, _) =>
        {
            ShowPlatformGestures();
            Screen.Focus();
        };
    }

    /// <summary>One definition, two renderers, exactly one of them live. Hiding the classic menu under the
    /// native strategy is required rather than tidy: measured on macOS, a NativeMenu installed while the classic
    /// Menu was still visible drew both — an in-window bar beneath the system bar.</summary>
    private void ApplyMenuStrategy(bool useNativeMenu)
    {
        ClassicMenu.IsVisible = !useNativeMenu;
        NativeBar.IsVisible = useNativeMenu;

        // Hiding NativeMenuBar is not enough to turn the native path off, because NativeMenuBar is not what
        // exports the menu. A window's NativeMenu goes out through the window's own
        // ITopLevelNativeMenuExporter — NativeMenu.MenuProperty's own change handler calls SetNativeMenu on it —
        // and NativeMenuBar only *consumes* the same property to draw an in-window fallback. Left attached under
        // the classic strategy, the definition would still install AppKit key equivalents and draw a system menu
        // bar on macOS beside the in-window one, and would still be handed to a Linux global-menu registrar
        // (Plasma's Application Menu applet, Unity) while the classic bar drew it in-window too. LIZTERM_MENU
        // exists so that a native gesture swallowing a 3270 key can be turned off; it has to actually turn it
        // off. Detaching is safe on both backends: each treats a null menu as an empty one.
        if (!useNativeMenu) NativeMenu.SetMenu(this, null);

        // macOS puts About in the application menu, so neither renderer's Help item may also carry one. Both get
        // the rule: LIZTERM_MENU=classic on macOS is reachable, and there the classic bar renders in-window while
        // the application menu still supplies its own About. Under that strategy the lookup answers null,
        // because the line above detached the menu — which is the point, not an omission. MenuLookup.Required
        // answers null for that and only that: a menu that is there and does not declare the item throws.
        //
        // The separator above About goes with it, in both menus. Nothing collapses a trailing divider, so an
        // About hidden on its own leaves the Help menu ending in one.
        var aboutInHelp = MenuStrategy.AboutInHelpMenu(OperatingSystem.IsMacOS());
        AboutMenuItem.IsVisible = aboutInHelp;
        AboutSeparator.IsVisible = aboutInHelp;
        if (MenuLookup.Required(NativeMenu.GetMenu(this), "_Help", "_About LizTerm...") is { } nativeAbout)
        {
            nativeAbout.IsVisible = aboutInHelp;
            if (MenuLookup.SeparatorAbove(nativeAbout) is { } separator) separator.IsVisible = aboutInHelp;
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

    // Deliberately the view model's methods, never the [RelayCommand]s. Each method carries its own guard; the
    // commands keep CommunityToolkit's default of disabling while running, which is fine for a click and wrong
    // for a keystroke — and on macOS Task 6's gestures make these keystrokes, activated by the OS.
    private void OnCopyClickNative(object? sender, EventArgs e) => _ = ViewModel?.CopyAsync();

    private void OnPasteClickNative(object? sender, EventArgs e) => _ = ViewModel?.PasteAsync();

    private void OnSelectAllClickNative(object? sender, EventArgs e) => ViewModel?.SelectAll();

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

    /// <summary>Menu gesture text from the platform table, so macOS shows Cmd and the others show Ctrl.
    /// The native items take a real Gesture rather than display text: on macOS that is an AppKit key
    /// equivalent, dispatched by the OS before the focused screen sees the key. That is safe for exactly these
    /// three, which TerminalScreen already routes away from the host, and is why nothing on File, Keys or Help
    /// carries one.
    ///
    /// Under the classic strategy ApplyMenuStrategy has detached the native menu, so the three classic
    /// InputGesture assignments still run and the three native ones find no item and do nothing — no key
    /// equivalent is installed for a menu that is not exported.</summary>
    private void ShowPlatformGestures()
    {
        var hotkeys = this.GetPlatformSettings()?.HotkeyConfiguration;
        if (hotkeys is null) return;
        CopyMenuItem.InputGesture = hotkeys.Copy.FirstOrDefault();
        PasteMenuItem.InputGesture = hotkeys.Paste.FirstOrDefault();
        SelectAllMenuItem.InputGesture = hotkeys.SelectAll.FirstOrDefault();

        // Null under the classic strategy, where the menu is detached and there is nothing to install a key
        // equivalent on. Required draws the line the plain lookup could not: null here means only that, and a
        // menu missing one of these three items throws rather than dropping its gesture silently.
        var menu = NativeMenu.GetMenu(this);
        Gesture(menu, "_Copy", hotkeys.Copy.FirstOrDefault());
        Gesture(menu, "_Paste", hotkeys.Paste.FirstOrDefault());
        Gesture(menu, "Select _All", hotkeys.SelectAll.FirstOrDefault());

        static void Gesture(NativeMenu? menu, string child, KeyGesture? gesture)
        {
            if (MenuLookup.Required(menu, "_Edit", child) is { } item) item.Gesture = gesture;
        }
    }

    private SessionViewModel? ViewModel => DataContext as SessionViewModel;

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
            vm.ErrorMessage = "Could not open the File Transfer dialog: " + ex.Message;
        }
        Screen.Focus();
    }
}
