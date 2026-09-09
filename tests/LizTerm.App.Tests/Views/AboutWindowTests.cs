// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Views;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Views;

public class AboutWindowTests
{
    [AvaloniaFact]
    public void Shows_version_engine_and_licenses()
    {
        var engine = new EngineInfo("b3270", "4.5.6 (fake)", "/opt/homebrew/bin/b3270", EngineSource.Override);
        var window = new AboutWindow("0.3.0", engine, "LIZTERM_B3270_PATH");
        window.Show();
        // Spec 8: the engine path wraps, so the window grows with it instead of clipping at a fixed height.
        Assert.Equal(SizeToContent.Height, window.SizeToContent);
        Assert.Equal(220, window.FindControl<TextBox>("LicensesText")!.Height);
        Assert.Equal("Version 0.3.0", window.FindControl<TextBlock>("VersionText")!.Text);
        Assert.Equal("b3270 4.5.6 (fake), from LIZTERM_B3270_PATH", window.FindControl<TextBlock>("EngineText")!.Text);
        Assert.Equal("/opt/homebrew/bin/b3270", window.FindControl<TextBlock>("EnginePathText")!.Text);
    }

    /// <summary>The credit line is the one piece of this that is readable without scrolling, so it carries the
    /// two facts a user is most likely to want: who owns the work and under what terms.</summary>
    [AvaloniaFact]
    public void Names_its_own_copyright_and_license_without_scrolling()
    {
        var window = Shown();
        Assert.Equal("Copyright 2026 by CoffeeMuse - BSD-3-Clause", window.FindControl<TextBlock>("CopyrightText")!.Text);
    }

    /// <summary>Until the license was chosen this box held third-party notices only, and the polish spec's
    /// assertion was that LizTerm claimed no license of its own. It is decided now, and this box is the only
    /// place the terms reach a user: LICENSE is embedded here and copied into no archive or installer.</summary>
    [AvaloniaFact]
    public void Carries_lizterms_own_license_ahead_of_the_third_party_notices()
    {
        var licenses = Shown().FindControl<TextBox>("LicensesText")!.Text!;
        Assert.StartsWith("LizTerm, BSD-3-Clause", licenses);
        Assert.Contains("Copyright 2026 by CoffeeMuse", licenses);
        Assert.Contains("THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS", licenses);
        Assert.True(licenses.IndexOf("Copyright 2026 by CoffeeMuse", StringComparison.Ordinal)
                    < licenses.IndexOf("Paul Mattes", StringComparison.Ordinal),
            "LizTerm's own terms belong ahead of the bundled components'.");
    }

    [AvaloniaFact]
    public void Still_carries_every_bundled_components_notice()
    {
        var licenses = Shown().FindControl<TextBox>("LicensesText")!.Text!;
        Assert.Contains("Paul Mattes", licenses);
        Assert.Contains("3270font", licenses);
        Assert.Contains("Avalonia", licenses);
        Assert.Contains("CommunityToolkit", licenses);
    }

    private static AboutWindow Shown()
    {
        var window = new AboutWindow("0.3.0", new EngineInfo("b3270", "4.5.6", "/path/to/b3270", EngineSource.Bundled), "");
        window.Show();
        return window;
    }
}
