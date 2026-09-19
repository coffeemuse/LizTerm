// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;

namespace LizTerm.App.Tests.Dialogs;

/// <summary>The words on the dialog (#151) live in the request, so the window only displays them and a test can
/// read them without one. The buttons are verbs, not Yes and No: the safe one is named for what it keeps.</summary>
public class ClosePromptRequestTests
{
    [Fact]
    public void A_window_close_names_the_session_and_what_closing_does()
    {
        var request = ClosePromptRequest.ForWindow("MVS/CE");

        Assert.Equal("Close MVS/CE?", request.Title);
        Assert.Equal("This session is still connected. Closing the window will disconnect it immediately.", request.Message);
        Assert.Equal("Disconnect", request.DisconnectLabel);
        Assert.Equal("Keep Connected", ClosePromptRequest.KeepLabel);
    }

    [Fact]
    public void A_quit_counts_the_connected_sessions()
    {
        var request = ClosePromptRequest.ForQuit(2);

        Assert.Equal("Quit LizTerm?", request.Title);
        Assert.Equal("2 sessions are still connected. Quitting will disconnect them immediately.", request.Message);
        Assert.Equal("Disconnect and Quit", request.DisconnectLabel);
    }

    [Fact]
    public void One_connected_session_reads_in_the_singular()
    {
        Assert.Equal("1 session is still connected. Quitting will disconnect it immediately.", ClosePromptRequest.ForQuit(1).Message);
    }
}
