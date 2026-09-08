using Avalonia.Controls;
using LizTerm.App.Startup;

namespace LizTerm.App.Tests.Startup;

/// <summary>The decision that keeps a shutdown from being cancelled by the window it is closing. It is asserted
/// here rather than through the real path because nothing in a test can make Avalonia close a window with a
/// shutdown reason: Window.CloseCore is internal and the reasons come from
/// ClassicDesktopStyleApplicationLifetime.DoShutdown, which would take the headless test session down with it.</summary>
public class ShutdownPolicyTests
{
    /// <summary>Avalonia reports OSShutdown, not ApplicationShutdown, when the shutdown came from the platform,
    /// so a test for one member alone would leave the other closing the app back into the picker.</summary>
    [Theory]
    [InlineData(WindowCloseReason.ApplicationShutdown)]
    [InlineData(WindowCloseReason.OSShutdown)]
    public void The_shutdown_reasons_are_a_shutdown(WindowCloseReason reason) =>
        Assert.True(ShutdownPolicy.IsShutdown(reason));

    [Theory]
    [InlineData(WindowCloseReason.WindowClosing)]
    [InlineData(WindowCloseReason.OwnerWindowClosing)]
    [InlineData(WindowCloseReason.Undefined)]
    public void Every_other_reason_is_the_user_closing_a_window(WindowCloseReason reason) =>
        Assert.False(ShutdownPolicy.IsShutdown(reason));

    /// <summary>Fails if Avalonia adds a reason: every member has to be classified deliberately, because the
    /// default here — treating a new shutdown reason as an ordinary close — is the failing one.</summary>
    [Fact]
    public void Every_reason_avalonia_defines_is_accounted_for()
    {
        var known = new[]
        {
            WindowCloseReason.Undefined, WindowCloseReason.WindowClosing, WindowCloseReason.OwnerWindowClosing,
            WindowCloseReason.ApplicationShutdown, WindowCloseReason.OSShutdown,
        };

        Assert.Equal(known, Enum.GetValues<WindowCloseReason>());
    }

    /// <summary>The bug this exists for: Avalonia's own Quit calls TryShutdown, which closes every window and
    /// then refuses to exit if any is left open, so the picker must not be opened from inside that close.</summary>
    [Fact]
    public void A_shutdown_close_does_not_reopen_the_picker()
    {
        Assert.False(ShutdownPolicy.UserClosedLastWindow(quitting: false, shutdownClose: true, openSessions: 0));
        Assert.True(ShutdownPolicy.UserClosedLastWindow(quitting: false, shutdownClose: false, openSessions: 0));
    }

    [Fact]
    public void Our_own_quit_and_a_surviving_session_still_keep_it_shut()
    {
        Assert.False(ShutdownPolicy.UserClosedLastWindow(quitting: true, shutdownClose: false, openSessions: 0));
        Assert.False(ShutdownPolicy.UserClosedLastWindow(quitting: false, shutdownClose: false, openSessions: 1));
    }
}
