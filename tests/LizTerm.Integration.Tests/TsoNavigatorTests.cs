// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Integration.Tests;

/// <summary>The navigator's text rules on screens copied from MVS/CE (Wally ISPF V2.2), no host needed.</summary>
public class TsoNavigatorTests
{
    /// <summary>What PF3 on Wally ISPF's primary menu leaves: the panel, with READY written over its second row.</summary>
    private const string LeftoverPanel =
        "                      Wally ISPF Primary Option Menu    UNIDENTIFIED INPUT FIELD\n" +
        " READY\n" +
        "  Option ===>\n" +
        "  0  Settings     Specify terminal and user parms           USERID   : MVSCE02\n" +
        "       Enter X to terminate ISPF using log and list defaults\n";

    [Fact]
    public void Ready_written_over_a_leftover_panel_is_recognised_and_is_not_plain_ready()
    {
        Assert.True(TsoNavigator.IsReadyOverLeftoverPanel(LeftoverPanel));
        Assert.False(TsoNavigator.IsAtReady(LeftoverPanel));
    }

    [Fact]
    public void A_live_panel_is_not_a_leftover()
    {
        const string live = "  Option ===>\n  6  Command      Enter TSO command or CLIST\n       READY TO GO\n";
        Assert.False(TsoNavigator.IsReadyOverLeftoverPanel(live));
    }

    [Fact]
    public void Plain_ready_is_not_a_leftover_panel()
    {
        const string ready = " USE COMMAND ISPF TO ACCESS ISPF\n READY\n";
        Assert.False(TsoNavigator.IsReadyOverLeftoverPanel(ready));
        Assert.True(TsoNavigator.IsAtReady(ready));
    }
}
