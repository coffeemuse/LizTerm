using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Views;

/// <summary>The File Transfer dialog. The caller sets DataContext to a <see cref="FileTransferViewModel"/> (from
/// <see cref="SessionViewModel.CreateTransfer"/>); the three panels switch on its phase.</summary>
public partial class FileTransferWindow : Window
{
    public FileTransferWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private FileTransferViewModel? ViewModel => DataContext as FileTransferViewModel;

    /// <summary>A running transfer is never orphaned behind a closed dialog: closing asks the engine to cancel and
    /// keeps the window until the Done panel shows the outcome.</summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (ViewModel is { IsRunning: true } vm)
        {
            e.Cancel = true;
            vm.CancelTransferCommand.Execute(null);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
