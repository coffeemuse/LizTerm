// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>The host rang and we responded, assertable without a speaker or a screen: the view model's own BellRang
/// stands for the flash, FakeBellRinger for the sound (bell spec §3.4).</summary>
public class SessionViewModelBellTests
{
    private static (SessionViewModel Vm, FakeEmulatorSession Session, FakeBellRinger Ringer, SettingsViewModel Settings, List<int> Flashes) Create()
    {
        var session = new FakeEmulatorSession();
        var ringer = new FakeBellRinger();
        var settings = new SettingsViewModel();
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard(), settings: settings, bellRinger: ringer);
        var flashes = new List<int>();
        vm.BellRang += (_, _) => flashes.Add(flashes.Count);
        return (vm, session, ringer, settings, flashes);
    }

    [Fact]
    public void By_default_a_bell_flashes_and_makes_no_sound()
    {
        var (_, session, ringer, _, flashes) = Create();

        session.RaiseBell();

        Assert.Single(flashes);
        Assert.Empty(ringer.Rings);
    }

    [Fact]
    public void With_the_system_alert_chosen_a_bell_rings_it()
    {
        var (_, session, ringer, settings, flashes) = Create();
        settings.BellSound = BellSound.SystemAlert;

        session.RaiseBell();

        Assert.Single(flashes);
        Assert.Equal([BellSound.SystemAlert], ringer.Rings);
    }

    [Fact]
    public void With_the_flash_off_a_bell_raises_nothing_for_the_window()
    {
        var (_, session, ringer, settings, flashes) = Create();
        settings.VisualBell = false;
        settings.BellSound = BellSound.SystemAlert;

        session.RaiseBell();

        Assert.Empty(flashes);
        Assert.Equal([BellSound.SystemAlert], ringer.Rings);
    }

    /// <summary>One gate in front of both outputs: two bells in the same instant are one bell.</summary>
    [Fact]
    public void A_second_bell_inside_the_interval_is_dropped_for_both_outputs()
    {
        var (_, session, ringer, settings, flashes) = Create();
        settings.BellSound = BellSound.SystemAlert;

        session.RaiseBell();
        session.RaiseBell();

        Assert.Single(flashes);
        Assert.Single(ringer.Rings);
    }

    [Fact]
    public void The_interval_is_photosensitive_safe()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(500), SessionViewModel.BellInterval);
        Assert.True(SessionViewModel.BellInterval >= TimeSpan.FromMilliseconds(500), "WCAG 2.3.1: never more than three flashes per second");
    }

    [Fact]
    public async Task After_dispose_a_bell_does_nothing()
    {
        var (vm, session, ringer, settings, flashes) = Create();
        settings.BellSound = BellSound.SystemAlert;
        await vm.DisposeAsync();

        session.RaiseBell();

        Assert.Empty(flashes);
        Assert.Empty(ringer.Rings);
    }

    [Fact]
    public void A_ringer_that_throws_puts_its_message_in_the_banner_and_the_flash_still_happens()
    {
        var (vm, session, ringer, settings, flashes) = Create();
        settings.BellSound = BellSound.SystemAlert;
        ringer.Exception = new InvalidOperationException("no speaker");

        session.RaiseBell();

        Assert.Single(flashes);
        Assert.Equal("Could not play the bell: no speaker", vm.ErrorMessage);
    }

    /// <summary>A P/Invoke that failed will fail again: the message is posted once, the ringer is not asked again,
    /// and the flash keeps working. Two bells here are separated by more than BellInterval so the throttle admits
    /// both; the second is what proves the latch.</summary>
    [Fact]
    public async Task A_failing_ringer_is_reported_once_and_not_called_again()
    {
        var (vm, session, ringer, settings, flashes) = Create();
        settings.BellSound = BellSound.SystemAlert;
        ringer.Exception = new InvalidOperationException("no speaker");

        session.RaiseBell();
        vm.ErrorMessage = null;
        await Task.Delay(SessionViewModel.BellInterval + TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        session.RaiseBell();

        Assert.Equal(2, flashes.Count);
        Assert.Single(ringer.Rings);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void Without_a_ringer_the_sound_setting_is_ignored_and_the_flash_still_happens()
    {
        var session = new FakeEmulatorSession();
        var settings = new SettingsViewModel { BellSound = BellSound.SystemAlert };
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard(), settings: settings);
        var flashes = 0;
        vm.BellRang += (_, _) => flashes++;

        session.RaiseBell();

        Assert.Equal(1, flashes);
        Assert.Null(vm.ErrorMessage);
    }
}
