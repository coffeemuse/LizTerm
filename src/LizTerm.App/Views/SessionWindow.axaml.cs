using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LizTerm.App.Files;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Views;

public partial class SessionWindow : Window
{
    public SessionWindow()
    {
        InitializeComponent();
        Screen.KeyRequested += (_, key) => _ = ViewModel?.SendKeyCommand.ExecuteAsync(key);
        Screen.TextEntered += (_, text) => _ = ViewModel?.TypeTextAsync(text);
        Screen.CellClicked += (_, cell) => _ = ViewModel?.MoveCursorAsync(cell.Row, cell.Column);
        Screen.CopyRequested += (_, _) => _ = ViewModel?.CopyCommand.ExecuteAsync(null);
        Screen.PasteRequested += (_, _) => _ = ViewModel?.PasteCommand.ExecuteAsync(null);
        Screen.SelectAllRequested += (_, _) => ViewModel?.SelectAllCommand.Execute(null);
        Opened += (_, _) =>
        {
            ShowPlatformGestures();
            Screen.Focus();
        };
    }

    /// <summary>Menu gesture text from the platform table, so macOS shows Cmd and the others show Ctrl.</summary>
    private void ShowPlatformGestures()
    {
        var hotkeys = this.GetPlatformSettings()?.HotkeyConfiguration;
        if (hotkeys is null) return;
        CopyMenuItem.InputGesture = hotkeys.Copy.FirstOrDefault();
        PasteMenuItem.InputGesture = hotkeys.Paste.FirstOrDefault();
        SelectAllMenuItem.InputGesture = hotkeys.SelectAll.FirstOrDefault();
    }

    private SessionViewModel? ViewModel => DataContext as SessionViewModel;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnNewSessionClick(object? sender, RoutedEventArgs e) => (Avalonia.Application.Current as App)?.ShowPicker();

    /// <summary>Opens the File Transfer dialog modally over this window. The dialog's own picker parents the OS
    /// file dialogs; the view model comes from the session view model so the last request is remembered.</summary>
    private async void OnFileTransferClick(object? sender, RoutedEventArgs e)
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
