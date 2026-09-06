using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using LizTerm.App.Dialogs;
using LizTerm.App.Views;
using LizTerm.Core.Security;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Views;

public class CertificateWindowTests
{
    private static readonly string[] Reason = ["TLS: Host certificate verification failed:", "self-signed certificate (18)"];
    private static readonly PresentedCertificate Presented = new("8C:13:6A:01", "O = tn3270proxy quick-start, CN = localhost", "pem", true, null);

    private static CertificatePromptRequest Request(PresentedCertificate? presented = null, string? fetchError = null,
        CertificatePin? previous = null, bool canPin = false, string? cannotPinReason = null) =>
        new("mvs.local", Reason, presented, fetchError, previous, canPin, cannotPinReason);

    private static string Text(Window window, string name) => window.FindControl<TextBlock>(name)!.Text ?? "";
    private static bool Visible(Window window, string name) => window.FindControl<Control>(name)!.IsVisible;

    [AvaloniaFact]
    public void A_first_time_failure_shows_the_presented_certificate_and_offers_to_trust_it()
    {
        var window = new CertificateWindow(Request(Presented, canPin: true));
        window.Show();
        Assert.Equal("Certificate not verified", window.Title);
        Assert.Equal("mvs.local presented a certificate that could not be verified:", Text(window, "HostText"));
        Assert.False(Visible(window, "TrustedText"));
        Assert.True(Visible(window, "PresentedText"));
        Assert.Equal("Presented: SHA-256 8C:13:6A:01", Text(window, "PresentedText"));
        Assert.Equal("Subject: O = tn3270proxy quick-start, CN = localhost", Text(window, "SubjectText"));
        Assert.False(Visible(window, "FetchErrorText"));
        Assert.Equal(string.Join("\n", Reason), Text(window, "ReasonText"));
        var remember = window.FindControl<CheckBox>("RememberBox")!;
        Assert.True(remember.IsVisible);
        Assert.Equal("Trust this certificate for this profile", remember.Content);
        Assert.False(Visible(window, "CannotPinText"));
    }

    [AvaloniaFact]
    public void A_changed_certificate_shows_both_fingerprints()
    {
        var previous = new CertificatePin("00:11:22:33", "CN=old", "old");
        var window = new CertificateWindow(Request(Presented, previous: previous, canPin: true));
        window.Show();
        Assert.Equal("Certificate changed", window.Title);
        Assert.Equal("mvs.local presented a certificate that is not the one trusted for this profile.", Text(window, "HostText"));
        Assert.True(Visible(window, "TrustedText"));
        Assert.Equal("Trusted: SHA-256 00:11:22:33", Text(window, "TrustedText"));
        Assert.Equal("Presented: SHA-256 8C:13:6A:01", Text(window, "PresentedText"));
        Assert.True(window.FindControl<CheckBox>("RememberBox")!.IsVisible);
    }

    [AvaloniaFact]
    public void A_failed_fetch_explains_and_hides_the_checkbox()
    {
        var window = new CertificateWindow(Request(fetchError: "No TLS answer from mvs.local:992 within 10 s."));
        window.Show();
        Assert.False(Visible(window, "PresentedText"));
        Assert.False(Visible(window, "SubjectText"));
        Assert.True(Visible(window, "FetchErrorText"));
        Assert.Equal("The certificate could not be read: No TLS answer from mvs.local:992 within 10 s.", Text(window, "FetchErrorText"));
        Assert.False(window.FindControl<CheckBox>("RememberBox")!.IsVisible);
        Assert.False(Visible(window, "CannotPinText"));
    }

    /// <summary>A pin the engine rejected is not a change: same fingerprint on both lines, the plain title, and the
    /// reason line instead of the checkbox.</summary>
    [AvaloniaFact]
    public void A_rejected_pin_with_the_same_fingerprint_is_not_called_a_change()
    {
        var previous = new CertificatePin(Presented.Sha256, Presented.Subject, "pem");
        const string reason = "The engine rejected the pinned certificate; connecting anyway applies to this attempt only.";
        var window = new CertificateWindow(Request(Presented, previous: previous, cannotPinReason: reason));
        window.Show();
        Assert.Equal("Certificate not verified", window.Title);
        Assert.Equal("mvs.local presented a certificate that could not be verified:", Text(window, "HostText"));
        Assert.True(Visible(window, "TrustedText"));
        Assert.Equal($"Trusted: SHA-256 {Presented.Sha256}", Text(window, "TrustedText"));
        Assert.Equal($"Presented: SHA-256 {Presented.Sha256}", Text(window, "PresentedText"));
        Assert.False(window.FindControl<CheckBox>("RememberBox")!.IsVisible);
        Assert.Equal(reason, Text(window, "CannotPinText"));
    }

    [AvaloniaFact]
    public void A_certificate_that_cannot_be_pinned_says_so_instead_of_the_checkbox()
    {
        const string reason = "This certificate cannot be pinned: the chain is missing its root. Connect Anyway applies to this attempt only.";
        var notPinnable = Presented with { Pinnable = false, NotPinnableReason = "the chain is missing its root" };
        var window = new CertificateWindow(Request(notPinnable, cannotPinReason: reason));
        window.Show();
        Assert.False(window.FindControl<CheckBox>("RememberBox")!.IsVisible);
        Assert.True(Visible(window, "CannotPinText"));
        Assert.Equal(reason, Text(window, "CannotPinText"));
    }

    [AvaloniaFact]
    public void Escape_declines_and_the_checkbox_is_carried_on_connect_anyway()
    {
        var declined = new CertificateWindow(Request(Presented, canPin: true));
        declined.Show();
        declined.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.Equal(CertificateDecision.Declined, declined.Decision);

        var trusted = new CertificateWindow(Request(Presented, canPin: true));
        trusted.Show();
        trusted.FindControl<CheckBox>("RememberBox")!.IsChecked = true;
        trusted.ConnectAnyway();
        Assert.Equal(new CertificateDecision(true, true), trusted.Decision);

        var once = new CertificateWindow(Request(Presented, canPin: true));
        once.Show();
        once.ConnectAnyway();
        Assert.Equal(new CertificateDecision(true, false), once.Decision);
    }
}
