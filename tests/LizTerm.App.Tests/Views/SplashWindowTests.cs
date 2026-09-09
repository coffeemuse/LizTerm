// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
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

    /// <summary>The splash is the first thing a user sees and the only window many will see before the picker,
    /// so it is where the attribution belongs. Deliberately ASCII: this line renders in the 3270 font, whose
    /// coverage is not general (the status bar's padlock already needs a private-use codepoint).</summary>
    [AvaloniaFact]
    public void Shows_the_copyright_and_license()
    {
        var window = new SplashWindow("0.3.0", new SplashTiming(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2)));
        window.Show();
        var text = window.FindControl<TextBlock>("CopyrightText")!.Text!;
        Assert.Equal("Copyright 2026 by CoffeeMuse - BSD-3-Clause", text);
        Assert.All(text, c => Assert.InRange(c, ' ', '~'));
        window.Close();
    }

    /// <summary>The splash is the one window in the app with a fixed size and no scrolling, so a line added to it
    /// can silently push another off the bottom. Layout runs headlessly even though drawing does not, which makes
    /// this checkable here rather than by eye.</summary>
    [AvaloniaFact]
    public void The_copyright_line_fits_inside_the_fixed_size_window()
    {
        var window = new SplashWindow("0.3.0", new SplashTiming(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2)));
        window.Show();
        var copyright = window.FindControl<TextBlock>("CopyrightText")!;
        Assert.True(copyright.Bounds.Height > 0, "the copyright line was never given a size");

        var bottom = copyright.TranslatePoint(new Point(0, copyright.Bounds.Height), window)!.Value.Y;
        Assert.InRange(bottom, 0, window.Height);

        // And it has not been fitted in by growing over the mark: the two still occupy their own rows.
        var mark = window.FindControl<ContentControl>("SplashMark")!;
        var markBottom = mark.TranslatePoint(new Point(0, mark.Bounds.Height), window)!.Value.Y;
        Assert.True(markBottom < bottom - copyright.Bounds.Height,
            $"the mark (ending at {markBottom}) overlaps the copyright line (ending at {bottom})");
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
