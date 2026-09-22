// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Platform;

namespace LizTerm.App.Tests.Platform;

/// <summary>The pure half of the probe (#169): which quit Apple event means the login session is ending. The
/// AppKit half cannot run headless — no quit is ever in flight in a test process — so only its refusal to throw
/// is asserted, the way MacEscapeChordsTests covers its own override.</summary>
public class MacQuitReasonTests
{
    /// <summary>The reasons loginwindow gives, and the two dialog spellings beside them: every one names the
    /// login session or the machine ending, so LizTerm must go quietly rather than hold the logout up.</summary>
    [Theory]
    [InlineData(MacQuitReason.ReallyLogOut)]
    [InlineData(MacQuitReason.LogOut)]
    [InlineData(MacQuitReason.ShutDown)]
    [InlineData(MacQuitReason.Restart)]
    [InlineData(MacQuitReason.ShowShutdownDialog)]
    [InlineData(MacQuitReason.ShowRestartDialog)]
    public void A_quit_sent_because_the_session_is_ending_is_a_system_shutdown(uint reason)
    {
        Assert.True(MacQuitReason.EndsTheSession(MacQuitReason.QuitEventClass, MacQuitReason.QuitEventId, reason));
    }

    /// <summary>Quit All quits the applications and leaves the user logged in, so it is a user's quit like any
    /// other: there is no logout to interrupt, and a connected session is still worth asking about.</summary>
    [Fact]
    public void Quit_all_is_not_a_system_shutdown()
    {
        Assert.False(MacQuitReason.EndsTheSession(MacQuitReason.QuitEventClass, MacQuitReason.QuitEventId, MacQuitReason.QuitAll));
    }

    /// <summary>A quit event carrying no reason at all: an AppleScript quit, or anything else that asks the
    /// application to go. Zero is how the probe reports the missing attribute.</summary>
    [Fact]
    public void A_quit_with_no_reason_is_not_a_system_shutdown()
    {
        Assert.False(MacQuitReason.EndsTheSession(MacQuitReason.QuitEventClass, MacQuitReason.QuitEventId, 0));
    }

    /// <summary>Some other Apple event was being handled when the quit arrived, so it says nothing about this
    /// quit. Only the quit event's own reason counts.</summary>
    [Theory]
    [InlineData(MacQuitReason.QuitEventClass, 0x6F646F63u)]  // 'aevt'/'odoc', an open-document event
    [InlineData(0x4C697A54u, MacQuitReason.QuitEventId)]     // some other suite's 'quit'
    public void Only_the_quit_event_is_read(uint eventClass, uint eventId)
    {
        Assert.False(MacQuitReason.EndsTheSession(eventClass, eventId, MacQuitReason.ReallyLogOut));
    }

    /// <summary>Off macOS the probe answers without touching libobjc, which is not there to touch.</summary>
    [Fact]
    public void Off_macos_nothing_is_a_system_shutdown()
    {
        Assert.False(MacQuitReason.IsSystemShutdown(isMacOS: false));
    }

    /// <summary>The one thing the AppKit half can be asked headlessly: with no quit in flight it answers false,
    /// and a runtime it cannot read (no Foundation loaded in a test process) is that same false rather than an
    /// exception thrown into a window's Closing.</summary>
    [Fact]
    public void With_no_quit_in_flight_the_probe_answers_false()
    {
        Assert.False(MacQuitReason.IsSystemShutdown(OperatingSystem.IsMacOS()));
    }
}
