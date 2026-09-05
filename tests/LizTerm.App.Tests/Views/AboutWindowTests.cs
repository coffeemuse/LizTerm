using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Views;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Views;

public class AboutWindowTests
{
    [AvaloniaFact]
    public void Shows_version_engine_and_notices()
    {
        var engine = new EngineInfo("b3270", "4.5.6 (fake)", "/opt/homebrew/bin/b3270", EngineSource.Override);
        var window = new AboutWindow("0.3.0", engine, "LIZTERM_B3270_PATH");
        window.Show();
        Assert.Equal("Version 0.3.0", window.FindControl<TextBlock>("VersionText")!.Text);
        Assert.Equal("b3270 4.5.6 (fake), from LIZTERM_B3270_PATH", window.FindControl<TextBlock>("EngineText")!.Text);
        Assert.Equal("/opt/homebrew/bin/b3270", window.FindControl<TextBlock>("EnginePathText")!.Text);
        var notices = window.FindControl<TextBox>("NoticesText")!.Text!;
        Assert.Contains("Paul Mattes", notices);
        Assert.Contains("3270font", notices);
        Assert.Contains("Avalonia", notices);
        Assert.Contains("CommunityToolkit", notices);
        Assert.DoesNotContain("LizTerm is licensed", notices);
    }
}
