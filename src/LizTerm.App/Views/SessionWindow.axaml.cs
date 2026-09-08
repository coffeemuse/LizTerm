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

    /// <summary>One definition, two renderers, exactly one of them visible. Hiding the classic menu under the
    /// native strategy is required rather than tidy: measured on macOS, a NativeMenu installed while the classic
    /// Menu was still visible drew both — an in-window bar beneath the system bar.</summary>
    private void ApplyMenuStrategy(bool useNativeMenu)
    {
        ClassicMenu.IsVisible = !useNativeMenu;
        NativeBar.IsVisible = useNativeMenu;

        // macOS puts About in the application menu, so neither renderer's Help item may also carry one. Both get
        // the rule: LIZTERM_MENU=classic on macOS is reachable, and there the classic bar renders in-window while
        // the application menu still supplies its own About.
        var aboutInHelp = MenuStrategy.AboutInHelpMenu(OperatingSystem.IsMacOS());
        AboutMenuItem.IsVisible = aboutInHelp;
        var nativeAbout = MenuLookup.Item(NativeMenu.GetMenu(this), "_Help", "_About LizTerm...");
        if (nativeAbout is not null) nativeAbout.IsVisible = aboutInHelp;
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

    /// <summary>Menu gesture text from the platform table, so macOS shows Cmd and the others show Ctrl.
    /// The native items take a real Gesture rather than display text: on macOS that is an AppKit key
    /// equivalent, dispatched by the OS before the focused screen sees the key. That is safe for exactly these
    /// three, which TerminalScreen already routes away from the host, and is why nothing on File, Keys or Help
    /// carries one.</summary>
    private void ShowPlatformGestures()
    {
        var hotkeys = this.GetPlatformSettings()?.HotkeyConfiguration;
        if (hotkeys is null) return;
        CopyMenuItem.InputGesture = hotkeys.Copy.FirstOrDefault();
        PasteMenuItem.InputGesture = hotkeys.Paste.FirstOrDefault();
        SelectAllMenuItem.InputGesture = hotkeys.SelectAll.FirstOrDefault();

        var menu = NativeMenu.GetMenu(this);
        Gesture(menu, "_Copy", hotkeys.Copy.FirstOrDefault());
        Gesture(menu, "_Paste", hotkeys.Paste.FirstOrDefault());
        Gesture(menu, "Select _All", hotkeys.SelectAll.FirstOrDefault());

        static void Gesture(NativeMenu? menu, string child, KeyGesture? gesture)
        {
            var item = MenuLookup.Item(menu, "_Edit", child);
            if (item is not null) item.Gesture = gesture;
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
