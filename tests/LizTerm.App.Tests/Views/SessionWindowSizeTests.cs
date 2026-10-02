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
        int model, Size? room = null)
    {
        var session = new FakeEmulatorSession { Profile = new SessionProfile(Name: "Mod", Host: "h", Model: model) };
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard());
        var window = new SessionWindow { DataContext = vm };
        if (room is { } r) window.LimitOpeningSize(r);
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
