// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LizTerm.App.Controls;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.Controls;

/// <summary>The visual bell: a flash the control owns end to end, like blink (bell spec §4).</summary>
public class TerminalScreenBellTests
{
    private static (TerminalScreen Screen, Window Window) Show()
    {
        var screen = new TerminalScreen { Snapshot = ScreenSnapshot.Empty(24, 80) };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        return (screen, window);
    }

    [AvaloniaFact]
    public void The_flash_is_brief()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(120), TerminalScreen.BellFlashDuration);
    }

    [AvaloniaFact]
    public async Task Flash_lights_the_screen_and_clears_on_its_own()
    {
        var (screen, window) = Show();
        Assert.False(screen.BellFlashing);

        screen.Flash();
        Assert.True(screen.BellFlashing);
        TestRender.Repaint(window);

        for (var i = 0; i < 40 && screen.BellFlashing; i++)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.False(screen.BellFlashing, "the flash must clear on its own");
        TestRender.Repaint(window);
    }

    [AvaloniaFact]
    public void Leaving_the_tree_ends_a_flash_in_progress()
    {
        var (screen, window) = Show();
        screen.Flash();

        window.Content = null;

        Assert.False(screen.BellFlashing);
    }
}
