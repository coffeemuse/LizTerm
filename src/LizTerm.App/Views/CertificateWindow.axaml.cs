using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.Dialogs;

namespace LizTerm.App.Views;

/// <summary>Spec 5.3. Cancel is the default and Escape maps to it; closing with the title bar also declines.
/// The result travels both as <see cref="Decision"/> and as the ShowDialog result.</summary>
public partial class CertificateWindow : Window
{
    public CertificateWindow() : this("", [], false) { }

    public CertificateWindow(string host, IReadOnlyList<string> reason, bool canRemember)
    {
        InitializeComponent();
        HostText.Text = $"{host} presented a certificate that could not be verified:";
        ReasonText.Text = string.Join("\n", reason);
        RememberBox.IsVisible = canRemember;
    }

    public CertificateDecision? Decision { get; private set; }

    internal void ConnectAnyway() => Finish(new CertificateDecision(true, RememberBox.IsChecked == true));

    private void OnConnectAnywayClick(object? sender, RoutedEventArgs e) => ConnectAnyway();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Finish(CertificateDecision.Declined);

    private void Finish(CertificateDecision decision)
    {
        Decision = decision;
        Close(decision);
    }
}
