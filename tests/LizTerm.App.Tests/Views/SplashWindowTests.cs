using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
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

    [AvaloniaFact]
    public void A_key_press_closes_the_splash_once_the_minimum_has_passed()
    {
        var window = new SplashWindow("0.3.0", new SplashTiming(TimeSpan.Zero, TimeSpan.FromMinutes(5)));
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        Assert.False(closed);
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Assert.True(closed, "a key press after the minimum must close the splash");
    }

    [AvaloniaFact]
    public async Task The_splash_closes_on_its_own_at_the_maximum()
    {
        var window = new SplashWindow("0.3.0", new SplashTiming(TimeSpan.Zero, TimeSpan.FromMilliseconds(50)));
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        Assert.False(closed.Task.IsCompleted);
        for (var i = 0; i < 40 && !closed.Task.IsCompleted; i++)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(closed.Task.IsCompleted, "the splash must close at the maximum without input");
    }
}
