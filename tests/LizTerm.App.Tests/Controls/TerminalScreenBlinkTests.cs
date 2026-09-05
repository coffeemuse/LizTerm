using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
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

    /// <summary>Forces a frame. The headless render loop draws on demand, so a "did it repaint" assertion
    /// passes vacuously without this — nothing renders after the first frame.</summary>
    private static void Repaint(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
    }

    /// <summary>The blink timer's only action is a full repaint, so the per-run text of an unchanged screen has
    /// to survive one: re-segmenting and re-shaping all 1920 cells twice a second, for as long as anything on
    /// screen blinks, was the cost.</summary>
    [AvaloniaFact]
    public void A_repaint_of_the_same_screen_reuses_the_prepared_runs()
    {
        var screen = new TerminalScreen { Snapshot = WithBlink() };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        Repaint(window);
        Assert.Equal(1, screen.RunPlanBuilds);

        // Exactly what a blink phase flip does: repaint, same snapshot.
        screen.InvalidateVisual();
        Repaint(window);
        Assert.Equal(1, screen.RunPlanBuilds);

        // A new screen is new text, so it must be prepared again.
        screen.Snapshot = WithBlink();
        Repaint(window);
        Assert.Equal(2, screen.RunPlanBuilds);
    }

    /// <summary>Runs carry their laid-out rectangles and shaped text, so they cannot outlive the cell geometry
    /// they were built for.</summary>
    [AvaloniaFact]
    public void A_new_cell_geometry_rebuilds_the_prepared_runs()
    {
        var screen = new TerminalScreen { Snapshot = WithBlink() };
        var window = new Window { Width = 800, Height = 600, Content = screen };
        window.Show();
        Repaint(window);
        Assert.Equal(1, screen.RunPlanBuilds);

        screen.Measure(new Size(400, 600));
        screen.Arrange(new Rect(0, 0, 400, 600));
        Repaint(window);

        Assert.Equal(2, screen.RunPlanBuilds);
    }
}
