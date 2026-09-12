// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
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
        // The engine line and the copyright both wrap, above a fixed 220-high licences box, so the window
        // grows with its content instead of clipping at a fixed height.
        Assert.Equal(SizeToContent.Height, window.SizeToContent);
        Assert.Equal(220, window.FindControl<TextBox>("LicensesText")!.Height);
        Assert.Equal("Version 0.3.0", window.FindControl<TextBlock>("VersionText")!.Text);
        Assert.Equal("b3270 4.5.6 (fake), from LIZTERM_B3270_PATH", window.FindControl<TextBlock>("EngineText")!.Text);
    }

    /// <summary>The binary's full path was shown under the engine name from before the engine was built and
    /// bundled on all six RIDs, when "which binary is this actually running?" was live. It is not: the engine
    /// line already says bundled or names the override, and the path is a string the user cannot act on. The
    /// fixture is an override because that is the case where showing a path was most defensible.</summary>
    [AvaloniaFact]
    public void Names_the_engine_without_showing_its_path()
    {
        const string path = "/opt/homebrew/bin/b3270";
        var engine = new EngineInfo("b3270", "4.5.6 (fake)", path, EngineSource.Override);
        var window = new AboutWindow("0.3.0", engine, "LIZTERM_B3270_PATH");
        window.Show();
        Assert.All(window.GetVisualDescendants().OfType<TextBlock>(),
            block => Assert.DoesNotContain(path, block.Text ?? ""));
    }

    /// <summary>The credit line names the copyright and stops there. It used to carry the licence too, from when
    /// the box below held third-party notices only and LizTerm's own terms appeared nowhere else; the box now
    /// opens with "LizTerm, BSD-3-Clause" three lines under this one and the embedded LICENSE repeats the
    /// copyright a few lines after that, so the suffix was restating what is already on screen twice.</summary>
    [AvaloniaFact]
    public void Names_its_own_copyright_without_repeating_the_license_below_it()
    {
        var window = Shown();
        var credit = window.FindControl<TextBlock>("CopyrightText")!.Text;
        Assert.Equal("Copyright 2026 by CoffeeMuse", credit);
        Assert.DoesNotContain("BSD-3-Clause", credit);
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

    [AvaloniaFact]
    public void About_shows_the_app_icon_beside_the_name()
    {
        var image = Shown().FindControl<Image>("AboutMark");
        Assert.NotNull(image);
        Assert.NotNull(image!.Source);
    }

    private static AboutWindow Shown()
    {
        var window = new AboutWindow("0.3.0", new EngineInfo("b3270", "4.5.6", "/path/to/b3270", EngineSource.Bundled), "");
        window.Show();
        return window;
    }
}
