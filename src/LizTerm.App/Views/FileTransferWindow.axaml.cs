using Avalonia.Controls;
using Avalonia.Input;
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

    /// <summary>Closing a running transfer asks the engine to cancel and keeps the window until the Done panel
    /// shows the outcome; closing again while that answer is still pending lets the window go. The policy is
    /// <see cref="FileTransferViewModel.TryClose"/>.</summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (ViewModel is { } vm && !vm.TryClose()) e.Cancel = true;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Escape closes the dialog in every phase, through Closing and so through TryClose: on a running
    /// transfer the first Escape cancels and the window stays until the outcome shows, exactly like Close. Handled
    /// here rather than with IsCancel on a button because the Running panel has no Close button.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            Close();
            return;
        }
        base.OnKeyDown(e);
    }
}
