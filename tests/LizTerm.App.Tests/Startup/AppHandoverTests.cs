// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace LizTerm.App.Tests.Startup;

/// <summary>The picker handing over to the session it opened, through the internal seam OpenSession calls: OpenSession
/// itself builds a real engine session.</summary>
public class AppHandoverTests
{
    /// <summary>#190: under WSLg a closing window hands focus to the next Linux window in the stacking order, so the
    /// new session has to be showing already when the picker goes, or an older session window takes the focus and
    /// can cover it.</summary>
    [AvaloniaFact]
    public void The_new_window_is_already_showing_when_the_picker_closes()
    {
        var picker = new Window();
        picker.Show();
        var session = new Window();
        bool? sessionShowingAtPickerClose = null;
        picker.Closing += (_, _) => sessionShowingAtPickerClose = session.IsVisible;

        App.ShowInPlaceOf(session, picker);

        Assert.True(sessionShowingAtPickerClose);
        Assert.False(picker.IsVisible);
        Assert.True(session.IsVisible);
        session.Close();
    }

    /// <summary>A window that cannot be shown leaves the picker open, rather than the process running with no window
    /// at all under OnExplicitShutdown.</summary>
    [AvaloniaFact]
    public void A_window_that_cannot_be_shown_leaves_the_picker_open()
    {
        var picker = new Window();
        picker.Show();
        var session = new Window();
        session.Show();
        session.Close();

        Assert.ThrowsAny<InvalidOperationException>(() => App.ShowInPlaceOf(session, picker));

        Assert.True(picker.IsVisible);
        picker.Close();
    }
}
