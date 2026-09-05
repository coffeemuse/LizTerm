using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Controls;
using LizTerm.Core.Screen;

namespace LizTerm.App.Tests.Controls;

public class TerminalScreenBlinkTests
{
    private static ScreenSnapshot WithBlink()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(1, 2, "ALERT", HostColor.Red, null, CellRendition.Blink);
        return buffer.Snapshot();
    }

    [AvaloniaFact]
    public void Interval_is_photosensitive_safe()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(750), TerminalScreen.BlinkInterval);
        Assert.True(TerminalScreen.BlinkInterval >= TimeSpan.FromMilliseconds(500), "WCAG 2.3.1: never more than three flashes per second");
    }

    [AvaloniaFact]
    public void Timer_runs_only_while_the_snapshot_has_blinking_cells()
    {
        var screen = new TerminalScreen { Snapshot = ScreenSnapshot.Empty(24, 80) };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        Assert.False(screen.BlinkTimerRunning);

        screen.Snapshot = WithBlink();
        Assert.True(screen.BlinkTimerRunning);

        screen.Snapshot = ScreenSnapshot.Empty(24, 80);
        Assert.False(screen.BlinkTimerRunning);
        Assert.False(screen.BlinkHidden);
    }

    [AvaloniaFact]
    public void Leaving_the_tree_stops_the_timer_and_returning_restarts_it()
    {
        var screen = new TerminalScreen { Snapshot = WithBlink() };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        Assert.True(screen.BlinkTimerRunning);

        window.Content = null;
        Assert.False(screen.BlinkTimerRunning);

        window.Content = screen;
        Assert.True(screen.BlinkTimerRunning);
    }
}
