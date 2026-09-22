// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Startup;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Startup;

/// <summary>Whether launch says anything about keymap.json, and in what words (#168). Pure, so the decision is
/// tested without opening a window.</summary>
public class KeymapNoticeTests
{
    private static readonly StartupPlan Picker = new StartupPlan.OpenPicker();
    private static readonly StartupPlan Session =
        new StartupPlan.OpenSession(new SessionProfile { Name = "TSO", Host = "tk5.local", Port = 3270 }, FromStore: true);

    [Fact]
    public void Nothing_is_said_when_the_keymap_loaded()
    {
        Assert.Null(KeymapNotice.For(Picker, loadError: null, "/config/keymap.json"));
    }

    [Fact]
    public void The_picker_gets_the_notice_so_it_is_seen_without_opening_a_session()
    {
        var notice = KeymapNotice.For(Picker, "keymap.json could not be read. It is not valid JSON.", "/config/keymap.json");

        Assert.NotNull(notice);
        Assert.Equal("keymap.json could not be read. It is not valid JSON.", notice.Message);
        Assert.Equal("/config/keymap.json", notice.Path);
    }

    [Fact]
    public void A_session_opened_from_the_command_line_gets_it_too()
    {
        Assert.NotNull(KeymapNotice.For(Session, "keymap.json could not be read.", "/config/keymap.json"));
    }

    /// <summary>A startup error window is the app on its way out, with no engine: it quits when that window closes,
    /// so a second thing to read would only be in the way.</summary>
    [Fact]
    public void A_startup_error_says_nothing_about_the_keymap()
    {
        Assert.Null(KeymapNotice.For(new StartupPlan.ShowError("no engine"), "keymap.json could not be read.", "/config/keymap.json"));
    }

    /// <summary>An in-memory keymap has no file, so there is nothing to name and nothing to go and fix.</summary>
    [Fact]
    public void Nothing_is_said_without_a_file_to_name()
    {
        Assert.Null(KeymapNotice.For(Picker, "keymap.json could not be read.", filePath: null));
    }

    [Fact]
    public void It_points_at_the_tab_that_offers_the_way_out()
    {
        Assert.Contains("Preferences", KeymapNotice.Pointer);
        Assert.Contains("Keyboard", KeymapNotice.Pointer);
    }
}
