// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using LizTerm.App.Views;

namespace LizTerm.App.Tests.Views;

public class LizWindowTests
{
    /// <summary>The dedication is lettered into the picture itself, so a screen reader hears none of it unless the
    /// image's accessible name repeats it.</summary>
    [AvaloniaFact]
    public void Names_the_photo_with_the_dedication_lettered_into_it()
    {
        var window = new LizWindow();
        window.Show();
        var photo = window.FindControl<Image>("Photo")!;
        Assert.NotNull(photo.Source);
        var name = AutomationProperties.GetName(photo);
        Assert.Contains("Dedicated to Liz", name);
        Assert.Contains("2007", name);
    }

    [AvaloniaFact]
    public void Close_closes_the_window()
    {
        var window = new LizWindow();
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();

        window.FindControl<Button>("CloseButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(closed);
    }
}
