// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using LizTerm.App.Views;

namespace LizTerm.App.Tests.Views;

public class AppWindowIconTests
{
    /// <summary>App.axaml's style gives every window, subclass or not, the one icon it loaded. The headless platform
    /// stubs the icon's pixels, so the instance is what can be checked; which file it is, is
    /// Core.Tests' WindowIconTests.</summary>
    [AvaloniaFact]
    public void Every_window_gets_the_icon_the_app_style_sets()
    {
        var icon = Application.Current!.Styles.OfType<Style>()
            .SelectMany(s => s.Setters.OfType<Setter>())
            .Single(s => s.Property == Window.IconProperty)
            .Value;
        Assert.IsType<WindowIcon>(icon);

        var liz = new LizWindow();
        var plain = new Window();
        liz.Show();
        plain.Show();

        Assert.Same(icon, liz.Icon);
        Assert.Same(icon, plain.Icon);
    }
}
