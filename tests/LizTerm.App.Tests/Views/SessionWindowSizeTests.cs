// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Controls;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Views;

/// <summary>The window opens around the model's alternate screen, and a host switching between its default and
/// alternate screens keeps the cell size (#198).</summary>
public class SessionWindowSizeTests
{
    private static (SessionWindow Window, TerminalScreen Screen, SessionViewModel Vm, FakeEmulatorSession Session) Open(
        int model, Size? room = null, string? note = null)
    {
        var session = new FakeEmulatorSession { Profile = new SessionProfile(Name: "Mod", Host: "h", Model: model, Note: note) };
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard());
        var window = new SessionWindow { DataContext = vm };
        // A display whose working area leaves exactly that room, at 100%.
        if (room is { } r)
            window.LimitOpeningSize(new PixelRect(0, 0, (int)(r.Width + SessionWindow.FrameAllowance.Width), (int)(r.Height + SessionWindow.FrameAllowance.Height)), 1);
        window.Show();
        // Show sizes the window after its first layout pass; the arrange at that size is the next pass.
        window.UpdateLayout();
        return (window, window.FindControl<TerminalScreen>("Screen")!, vm, session);
    }

    [Fact]
    public void The_view_model_carries_the_profile_s_alternate_size()
    {
        var session = new FakeEmulatorSession { Profile = new SessionProfile(Model: 4) };
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard());
        Assert.Equal(new ScreenSize(43, 80), vm.AlternateSize);
    }

    [AvaloniaFact]
    public void The_screen_is_fitted_to_the_alternate_size_from_the_start()
    {
        var (_, screen, _, _) = Open(model: 4);
        Assert.Equal(new ScreenSize(43, 80), screen.AlternateSize);
    }

    [AvaloniaFact]
    public void A_model_4_window_opens_taller_than_a_model_2_window_at_the_same_cell_size()
    {
        var (model2, screen2, _, _) = Open(model: 2);
        var (model4, screen4, _, _) = Open(model: 4);

        Assert.Equal(TerminalScreen.PreferredFontSize, screen2.LastGeometry.FontSize);
        Assert.Equal(TerminalScreen.PreferredFontSize, screen4.LastGeometry.FontSize);
        Assert.Equal(model2.ClientSize.Width, model4.ClientSize.Width);
        Assert.Equal(19 * screen4.LastGeometry.CellHeight, model4.ClientSize.Height - model2.ClientSize.Height, 0);
    }

    /// <summary>Spoofy's report: TSO's logon switches a model 4 from its 24-row default screen to the 43-row
    /// alternate one, and the text used to shrink to fit.</summary>
    [AvaloniaFact]
    public void The_host_switching_to_the_alternate_screen_keeps_the_cell_size()
    {
        var (window, screen, _, session) = Open(model: 4);
        var before = screen.LastGeometry;

        session.RaiseScreen(ScreenSnapshot.Empty(43, 80));
        window.UpdateLayout();

        Assert.Equal(before, screen.LastGeometry);
    }

    /// <summary>Once open the window is the user's to size: a bar appearing under the screen takes its room from the
    /// screen rather than growing the window.</summary>
    [AvaloniaFact]
    public void Once_open_the_window_keeps_its_size_as_its_content_changes()
    {
        var (window, _, vm, _) = Open(model: 4);
        var opened = window.ClientSize;
        Assert.Equal(SizeToContent.Manual, window.SizeToContent);

        vm.ErrorMessage = "Something went wrong";
        window.UpdateLayout();

        Assert.Equal(opened, window.ClientSize);
    }

    /// <summary>On a display too small for the preferred size the window opens at the largest size that fits, and
    /// the limit goes once it is open, so the user can still make it larger.</summary>
    [AvaloniaFact]
    public void Limited_by_the_display_it_opens_at_the_largest_size_that_fits_and_drops_the_limit()
    {
        var (window, screen, _, _) = Open(model: 4, room: new Size(1200, 600));

        Assert.True(window.ClientSize.Height <= 600, $"height {window.ClientSize.Height}");
        Assert.True(screen.LastGeometry.FontSize < TerminalScreen.PreferredFontSize, $"font {screen.LastGeometry.FontSize}");
        Assert.True(double.IsPositiveInfinity(window.MaxWidth));
        Assert.True(double.IsPositiveInfinity(window.MaxHeight));
    }

    /// <summary>Capped by the display, the screen and the status bar settle on one size before the window opens,
    /// and the grid fits inside the screen.</summary>
    [AvaloniaTheory]
    [InlineData(500)]
    [InlineData(600)]
    [InlineData(700)]
    [InlineData(800)]
    [InlineData(900)]
    public void Limited_by_the_display_the_screen_and_the_bar_open_at_one_size(int height)
    {
        var (window, screen, _, _) = Open(model: 4, room: new Size(1200, height));

        var g = screen.LastGeometry;
        Assert.Equal(g.FontSize, screen.OiaFontSize);
        Assert.True(g.CellHeight * 43 <= screen.Bounds.Height + 0.01, $"grid {g.CellHeight * 43}, screen {screen.Bounds.Height}");
        Assert.True(window.ClientSize.Height <= height, $"height {window.ClientSize.Height}");
    }

    /// <summary>A profile with a note or tags shows a banner on every connect. The window opens with room for it,
    /// as the fixed 960x680 window had, so its arrival leaves the cell size alone.</summary>
    [AvaloniaFact]
    public void The_connect_banner_has_its_room_from_the_start()
    {
        var (window, screen, vm, session) = Open(model: 2, note: "LAN only");
        Assert.Equal(TerminalScreen.PreferredFontSize, screen.LastGeometry.FontSize);

        session.RaiseConnection(ConnectionState.ConnectedTn3270E);
        window.UpdateLayout();

        Assert.True(vm.IsBannerVisible);
        Assert.Equal(TerminalScreen.PreferredFontSize, screen.LastGeometry.FontSize);
    }

    /// <summary>Placed partly off the display it was sized for, the window is moved onto it once open, no further
    /// than it has to go.</summary>
    [AvaloniaFact]
    public void Once_open_the_window_is_kept_inside_the_working_area_it_was_sized_for()
    {
        var session = new FakeEmulatorSession { Profile = new SessionProfile(Name: "Mod", Host: "h", Model: 4) };
        var window = new SessionWindow { DataContext = new SessionViewModel(session, action => action(), new FakeTextClipboard()) };
        var area = new PixelRect(0, 25, 1440, 875);
        window.LimitOpeningSize(area, 1);
        window.Position = new PixelPoint(300, 600);
        window.Show();

        var frame = window.FrameSize ?? window.ClientSize;
        Assert.Equal(300, window.Position.X);
        Assert.True(window.Position.Y >= area.Y, $"top {window.Position.Y}");
        Assert.True(window.Position.Y + frame.Height <= area.Bottom + 0.01, $"bottom {window.Position.Y + frame.Height}");
    }

    [Theory]
    [InlineData(100, 200, 100, 200)]
    [InlineData(100, 500, 100, 300)]
    [InlineData(2000, 200, 1000, 200)]
    [InlineData(-50, -50, 0, 25)]
    [InlineData(100, 3000, 100, 300)]
    public void A_window_is_moved_no_further_than_it_must_to_lie_inside_the_area(int x, int y, int keptX, int keptY) =>
        Assert.Equal(new PixelPoint(keptX, keptY), SessionWindow.KeptInside(new PixelPoint(x, y), new PixelSize(440, 600), new PixelRect(0, 25, 1440, 875)));

    [Fact]
    public void A_window_larger_than_the_area_starts_at_its_top_left() =>
        Assert.Equal(new PixelPoint(0, 25), SessionWindow.KeptInside(new PixelPoint(300, 400), new PixelSize(2000, 1000), new PixelRect(0, 25, 1440, 875)));

    /// <summary>The room is the display's working area, in DIPs, less an allowance for the title bar and border
    /// the client size does not include.</summary>
    [Theory]
    [InlineData(1440, 875, 1.0, 1440 - 16, 875 - 48)]
    [InlineData(2880, 1750, 2.0, 1440 - 16, 875 - 48)]
    [InlineData(1920, 1040, 1.25, 1536 - 16, 832 - 48)]
    public void The_room_is_the_working_area_less_the_frame(int width, int height, double scaling, double roomWidth, double roomHeight)
    {
        var room = SessionWindow.OpeningRoom(new PixelRect(0, 25, width, height), scaling);
        Assert.Equal(new Size(roomWidth, roomHeight), room);
    }
}
