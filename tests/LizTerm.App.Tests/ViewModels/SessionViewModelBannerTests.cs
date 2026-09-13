// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Rendering;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>The connect banner and the status bar's note icon and chips (#93): the profile's note, name, host
/// and tags shown inside the session, for the moment you have forgotten which box you are on.</summary>
public class SessionViewModelBannerTests
{
    private static (SessionViewModel Vm, FakeEmulatorSession Session) Create(SessionProfile profile, TagRegistry? tags = null)
    {
        var session = new FakeEmulatorSession { Profile = profile };
        return (new SessionViewModel(session, a => a(), new FakeTextClipboard(), tags: tags), session);
    }

    private static SessionProfile Noted() =>
        new() { Name = "MVS/CE", Host = "10.42.37.209", Port = 3270, Note = "IND$FILE test box, no live data" };

    private static SessionProfile Tagged() =>
        new() { Name = "MVS/CE", Host = "10.42.37.209", Port = 3270, Tags = TagSet.From(["prod", "mvs", "FAVORITE"]) };

    [Fact]
    public void Connecting_shows_the_banner_when_the_profile_has_a_note()
    {
        var (vm, session) = Create(Noted());
        Assert.False(vm.IsBannerVisible);

        session.RaiseConnection(ConnectionState.Connected3270);

        Assert.True(vm.IsBannerVisible);
    }

    [Fact]
    public void Connecting_shows_the_banner_for_tags_alone()
    {
        var (vm, session) = Create(Tagged());

        session.RaiseConnection(ConnectionState.ConnectedTn3270E);

        Assert.True(vm.IsBannerVisible);
    }

    /// <summary>Name and host are already in the title; a banner saying only that would be noise on every
    /// Quick Connect session.</summary>
    [Fact]
    public void Connecting_shows_no_banner_for_a_profile_with_neither_note_nor_tags()
    {
        var (vm, session) = Create(new SessionProfile { Name = "tk5", Host = "h" });

        session.RaiseConnection(ConnectionState.Connected3270);

        Assert.False(vm.IsBannerVisible);
    }

    /// <summary>The banner reaches nothing before the socket is up: a connect attempt is not a session.</summary>
    [Fact]
    public void A_pending_connection_does_not_show_the_banner()
    {
        var (vm, session) = Create(Noted());

        session.RaiseConnection(ConnectionState.TcpPending);

        Assert.False(vm.IsBannerVisible);
    }

    [Fact]
    public async Task The_first_key_hides_the_banner()
    {
        var (vm, session) = Create(Noted());
        session.RaiseConnection(ConnectionState.Connected3270);

        await vm.SendKeyAsync(TerminalKey.Enter);

        Assert.False(vm.IsBannerVisible);
    }

    [Fact]
    public async Task Typed_text_hides_the_banner()
    {
        var (vm, session) = Create(Noted());
        session.RaiseConnection(ConnectionState.Connected3270);

        await vm.TypeTextAsync("a");

        Assert.False(vm.IsBannerVisible);
    }

    [Fact]
    public async Task A_click_on_the_screen_hides_the_banner()
    {
        var (vm, session) = Create(Noted());
        session.RaiseConnection(ConnectionState.Connected3270);

        await vm.MoveCursorAsync(1, 1);

        Assert.False(vm.IsBannerVisible);
    }

    /// <summary>b3270 reports Connected3270 and then ConnectedTn3270E for one session; the second report is
    /// not a new arrival and must not undo the keystroke that dismissed the banner.</summary>
    [Fact]
    public async Task A_second_connected_state_for_the_same_session_does_not_bring_the_banner_back()
    {
        var (vm, session) = Create(Noted());
        session.RaiseConnection(ConnectionState.Connected3270);
        await vm.SendKeyAsync(TerminalKey.Enter);

        session.RaiseConnection(ConnectionState.ConnectedTn3270E);

        Assert.False(vm.IsBannerVisible);
    }

    /// <summary>A reconnect is exactly when you are most likely to have lost track of the box.</summary>
    [Fact]
    public async Task A_reconnect_shows_the_banner_again()
    {
        var (vm, session) = Create(Noted());
        session.RaiseConnection(ConnectionState.Connected3270);
        await vm.SendKeyAsync(TerminalKey.Enter);
        session.RaiseConnection(ConnectionState.Disconnected);
        Assert.False(vm.IsBannerVisible);

        session.RaiseConnection(ConnectionState.Connected3270);

        Assert.True(vm.IsBannerVisible);
    }

    /// <summary>The status bar's icon: one click away in every session, even one with nothing but a name.</summary>
    [Fact]
    public async Task The_icon_shows_the_banner_for_any_profile_and_a_key_hides_it_again()
    {
        var (vm, session) = Create(new SessionProfile { Name = "tk5", Host = "h" });
        session.RaiseConnection(ConnectionState.Connected3270);
        Assert.False(vm.IsBannerVisible);

        vm.ShowBanner();
        Assert.True(vm.IsBannerVisible);

        await vm.SendKeyAsync(TerminalKey.Enter);
        Assert.False(vm.IsBannerVisible);
    }

    [Fact]
    public void The_note_is_trimmed_and_blank_reads_as_none()
    {
        var (noted, _) = Create(new SessionProfile { Name = "a", Host = "h", Note = "  LAN only  " });
        Assert.Equal("LAN only", noted.Note);
        Assert.True(noted.HasNote);

        var (blank, _) = Create(new SessionProfile { Name = "a", Host = "h", Note = "   " });
        Assert.Null(blank.Note);
        Assert.False(blank.HasNote);
    }

    [Fact]
    public void The_banner_names_the_host_and_port()
    {
        var (vm, _) = Create(Noted());
        Assert.Equal("10.42.37.209:3270", vm.HostPort);
    }

    /// <summary>The picker's rules, so the bar and the list can never disagree: uppercase text, the registry's
    /// colour, and FAVORITE never a chip.</summary>
    [Fact]
    public void Chips_take_the_registry_colours_uppercase_the_name_and_skip_favorite()
    {
        var registry = TagRegistry.Empty.Register(["prod"]).Registry.Recolour("prod", TagColor.Red);
        var (vm, _) = Create(Tagged(), registry);

        Assert.Equal(["PROD", "MVS"], vm.Chips.Select(c => c.Text));
        Assert.Same(TagPalette.Brush(TagColor.Red), vm.Chips[0].Background);
        Assert.Same(TagPalette.Brush(TagRegistry.UnregisteredColor), vm.Chips[1].Background);
    }

    /// <summary>No registry (tests, and any caller without one) still draws chips, in the unregistered grey.</summary>
    [Fact]
    public void Without_a_registry_every_chip_is_grey()
    {
        var (vm, _) = Create(Tagged());
        Assert.All(vm.Chips, c => Assert.Same(TagPalette.Brush(TagRegistry.UnregisteredColor), c.Background));
    }
}
