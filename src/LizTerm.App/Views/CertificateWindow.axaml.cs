using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.Dialogs;
using LizTerm.Core.Security;

namespace LizTerm.App.Views;

/// <summary>Spec 5.4. Cancel is the default and Escape maps to it; closing with the title bar also declines. The
/// result travels both as <see cref="Decision"/> and as the ShowDialog result.</summary>
public partial class CertificateWindow : Window
{
    /// <summary>Design-time only: a first-time failure with a pinnable certificate.</summary>
    public CertificateWindow() : this(new CertificatePromptRequest("mvs.example",
        ["TLS: Host certificate verification failed:", "self-signed certificate (18)"],
        new PresentedCertificate("8C:13:6A:01", "CN=mvs.example", "", true, null), null, null, true, null)) { }

    public CertificateWindow(CertificatePromptRequest request)
    {
        InitializeComponent();
        var changed = request.Previous is not null && request.Presented is not null
            && !string.Equals(request.Presented.Sha256, request.Previous.Sha256, StringComparison.OrdinalIgnoreCase);
        Title = changed ? "Certificate changed" : "Certificate not verified";
        HostText.Text = changed
            ? $"{request.Host} presented a certificate that is not the one trusted for this profile."
            : $"{request.Host} presented a certificate that could not be verified:";
        TrustedText.IsVisible = request.Previous is not null;
        TrustedText.Text = request.Previous is null ? "" : $"Trusted: SHA-256 {request.Previous.Sha256}";
        PresentedText.IsVisible = request.Presented is not null;
        SubjectText.IsVisible = request.Presented is not null;
        if (request.Presented is { } presented)
        {
            PresentedText.Text = $"Presented: SHA-256 {presented.Sha256}";
            SubjectText.Text = $"Subject: {presented.Subject}";
        }
        FetchErrorText.IsVisible = request.Presented is null && request.FetchError is not null;
        FetchErrorText.Text = request.FetchError is null ? "" : $"The certificate could not be read: {request.FetchError}";
        ReasonText.Text = string.Join("\n", request.Reason);
        RememberBox.IsVisible = request.CanPin;
        CannotPinText.IsVisible = request.CannotPinReason is not null;
        CannotPinText.Text = request.CannotPinReason ?? "";
    }

    public CertificateDecision? Decision { get; private set; }

    internal void ConnectAnyway() => Finish(new CertificateDecision(true, RememberBox.IsVisible && RememberBox.IsChecked == true));

    private void OnConnectAnywayClick(object? sender, RoutedEventArgs e) => ConnectAnyway();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Finish(CertificateDecision.Declined);

    private void Finish(CertificateDecision decision)
    {
        Decision = decision;
        Close(decision);
    }
}
