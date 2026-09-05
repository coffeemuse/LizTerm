using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using LizTerm.App.Dialogs;
using LizTerm.App.Views;

namespace LizTerm.App.Tests.Views;

public class CertificateWindowTests
{
    private static readonly string[] Reason = ["TLS: Host certificate verification failed:", "self-signed certificate (18)"];

    [AvaloniaFact]
    public void Shows_host_and_reason_and_hides_remember_for_ad_hoc_profiles()
    {
        var window = new CertificateWindow("mvs.local", Reason, canRemember: false);
        window.Show();
        Assert.Equal("Certificate not verified", window.Title);
        Assert.Equal("mvs.local presented a certificate that could not be verified:", window.FindControl<TextBlock>("HostText")!.Text);
        Assert.Equal(string.Join("\n", Reason), window.FindControl<TextBlock>("ReasonText")!.Text);
        Assert.False(window.FindControl<CheckBox>("RememberBox")!.IsVisible);
    }

    [AvaloniaFact]
    public void Escape_declines_and_remember_is_carried_on_connect_anyway()
    {
        var declined = new CertificateWindow("h", Reason, canRemember: true);
        declined.Show();
        Assert.True(declined.FindControl<CheckBox>("RememberBox")!.IsVisible);
        declined.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.Equal(CertificateDecision.Declined, declined.Decision);

        var accepted = new CertificateWindow("h", Reason, canRemember: true);
        accepted.Show();
        accepted.FindControl<CheckBox>("RememberBox")!.IsChecked = true;
        accepted.ConnectAnyway();
        Assert.Equal(new CertificateDecision(true, true), accepted.Decision);
    }
}
