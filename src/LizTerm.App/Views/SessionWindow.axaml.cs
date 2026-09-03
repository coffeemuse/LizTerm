using Avalonia.Controls;
using Avalonia.Interactivity;
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
        Opened += (_, _) => Screen.Focus();
    }

    private SessionViewModel? ViewModel => DataContext as SessionViewModel;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnNewSessionClick(object? sender, RoutedEventArgs e) => (Avalonia.Application.Current as App)?.ShowPicker();
}
