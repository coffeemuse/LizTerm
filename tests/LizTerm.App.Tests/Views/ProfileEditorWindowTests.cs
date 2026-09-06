using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Views;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Views;

public class ProfileEditorWindowTests
{
    [AvaloniaFact]
    public void The_pinned_line_shows_only_for_a_pinned_profile_and_forget_hides_it()
    {
        var pinned = new ProfileEditorWindow(new SessionProfile
        {
            Name = "gw", Host = "gw", UseTls = true, PinnedCertificate = new CertificatePin("8C:13", "CN=gw", "pem"),
        });
        pinned.Show();
        var panel = pinned.FindControl<StackPanel>("PinPanel")!;
        Assert.True(panel.IsVisible);
        Assert.Equal("Pinned certificate: SHA-256 8C:13", pinned.FindControl<TextBlock>("PinText")!.Text);
        pinned.FindControl<Button>("ForgetButton")!.Command!.Execute(null);
        Assert.False(panel.IsVisible);

        var plain = new ProfileEditorWindow(new SessionProfile { Name = "p", Host = "h" });
        plain.Show();
        Assert.False(plain.FindControl<StackPanel>("PinPanel")!.IsVisible);
        Assert.Equal("Backspace erases the previous character (off: Backspace only moves the cursor left)",
            plain.FindControl<CheckBox>("BackspaceBox")!.Content);
    }
}
