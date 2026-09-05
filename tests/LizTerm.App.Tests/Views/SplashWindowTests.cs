using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Startup;
using LizTerm.App.Views;

namespace LizTerm.App.Tests.Views;

public class SplashWindowTests
{
    [AvaloniaFact]
    public void Shows_the_mark_and_version_without_decorations()
    {
        var window = new SplashWindow("0.3.0", new SplashTiming(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2)));
        window.Show();
        Assert.Equal(WindowDecorations.None, window.WindowDecorations);
        Assert.Equal(480d, window.Width);
        Assert.Equal(300d, window.Height);
        Assert.Equal("Version 0.3.0", window.FindControl<TextBlock>("VersionText")!.Text);
        Assert.NotNull(window.FindControl<ContentControl>("SplashMark"));
        window.Close();
    }
}
