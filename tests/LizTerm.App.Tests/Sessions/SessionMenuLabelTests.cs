// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Sessions;
using LizTerm.App.Tests.Fakes;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Sessions;

/// <summary>Session switching spec §4's table, row for row.</summary>
public class SessionMenuLabelTests
{
    [Fact]
    public void A_connected_favourite_gets_its_access_key_a_star_and_its_host()
    {
        var (entry, _, _) = TestSessions.Create("TSO", "tk5.local", tags: ["FAVORITE"]);

        Assert.Equal("_3  ★ TSO - tk5.local", SessionMenuLabel.For(entry, 3));
    }

    [Fact]
    public void A_disconnected_session_says_so()
    {
        var (entry, _, _) = TestSessions.Create("CICS", "zxplore.example", ConnectionState.Disconnected);

        Assert.Equal("_4  CICS - zxplore.example (Disconnected)", SessionMenuLabel.For(entry, 4));
    }

    /// <summary>Position ten prints as 0, and a name that already carries its host does not repeat it — the
    /// window title's "sdf.example:3270 - sdf.example" is not copied.</summary>
    [Fact]
    public void The_tenth_session_is_zero_and_an_ad_hoc_name_does_not_repeat_its_host()
    {
        var (entry, _, _) = TestSessions.Create("sdf.example:3270", "sdf.example", isSaved: false);

        Assert.Equal("_0  sdf.example:3270", SessionMenuLabel.For(entry, 10));
    }

    [Fact]
    public void An_lu_at_host_name_does_not_repeat_its_host_either()
    {
        var (entry, _, _) = TestSessions.Create("CONS01@mvs.local:3270", "MVS.LOCAL", isSaved: false);

        Assert.Equal("_1  CONS01@mvs.local:3270", SessionMenuLabel.For(entry, 1));
    }

    /// <summary>The name-carries-host rule is for ad hoc sessions only: a saved name that happens to contain a
    /// short host still shows it.</summary>
    [Fact]
    public void A_saved_name_containing_its_host_still_shows_the_host()
    {
        var (entry, _, _) = TestSessions.Create("MVS/CE", "mvs");

        Assert.Equal("_2  MVS/CE - mvs", SessionMenuLabel.For(entry, 2));
    }

    /// <summary>Past ten there is no number and no leading spaces, and an underscore in the name is doubled so it
    /// cannot become an access key.</summary>
    [Fact]
    public void Past_ten_there_is_no_number_and_underscores_are_doubled()
    {
        var (entry, _, _) = TestSessions.Create("MVS_PROD", "mvs.local");

        Assert.Equal("MVS__PROD - mvs.local", SessionMenuLabel.For(entry, null));
    }
}
