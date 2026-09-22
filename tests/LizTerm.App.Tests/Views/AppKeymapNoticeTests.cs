// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Startup;
using LizTerm.App.Views;

namespace LizTerm.App.Tests.Views;

/// <summary>App putting the launch notice up over whatever the startup plan opened (#168), through the internal
/// seam that takes the notice, so no test here reads the user's own keymap.json.</summary>
public class AppKeymapNoticeTests
{
    private static readonly KeymapNotice Notice = new("keymap.json could not be read.", "/config/keymap.json");

    [AvaloniaFact]
    public void Nothing_opens_when_there_is_no_notice()
    {
        Assert.Null(((App)Application.Current!).ShowKeymapNotice(null, new Window()));
    }

    [AvaloniaFact]
    public void The_notice_opens_over_the_window_that_is_already_up()
    {
        var app = (App)Application.Current!;
        var owner = new Window();
        owner.Show();
        try
        {
            var notice = app.ShowKeymapNotice(Notice, owner);

            Assert.NotNull(notice);
            Assert.Equal(KeymapNotice.Title, notice.Title);
            notice.Close();
        }
        finally
        {
            owner.Close();
        }
    }

    /// <summary>A plan that opened no window has nothing to be modal over, and a notice alone in an empty app is
    /// not what a keymap problem deserves.</summary>
    [AvaloniaFact]
    public void Nothing_opens_when_the_plan_opened_no_window()
    {
        Assert.Null(((App)Application.Current!).ShowKeymapNotice(Notice, owner: null));
    }
}
