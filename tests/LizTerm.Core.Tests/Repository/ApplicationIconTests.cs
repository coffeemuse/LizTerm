// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Xml.Linq;

namespace LizTerm.Core.Tests.Repository;

/// <summary>Windows draws the Start menu, taskbar and Explorer icon from a Win32 icon resource inside the
/// executable, and the SDK embeds one only when the project declares ApplicationIcon. Three icon settings in
/// this repository look like they would cover it and none does: LizTerm.parcel's Win32Settings.InstallerIcon
/// dresses the installer rather than the application it installs, MacOsSettings.AppIcon and
/// LinuxSettings.AppIcon are the other two platforms, and Window.Icon is an Avalonia resource the desktop
/// shell cannot read. 0.5.0's rehearsal build shipped with the generic executable icon for exactly that
/// reason -- the installer carried the icon, the installed application did not -- so this holds the
/// declaration in place. It lives in Core.Tests because that project builds on every run.</summary>
public class ApplicationIconTests
{
    private static string ProjectPath() =>
        Path.Combine(LicenseHeader.Root(), "src", "LizTerm.App", "LizTerm.App.csproj");

    /// <summary>The declared value, verbatim, or null when the project declares none.</summary>
    private static string? Declared() =>
        XDocument.Load(ProjectPath())
            .Descendants("ApplicationIcon")
            .Select(e => e.Value.Trim())
            .FirstOrDefault();

    [Fact]
    public void The_app_project_declares_an_application_icon()
    {
        Assert.False(string.IsNullOrWhiteSpace(Declared()),
            "LizTerm.App.csproj declares no <ApplicationIcon>, so LizTerm.exe carries no Win32 icon resource "
            + "and Windows draws the generic executable icon in the Start menu, on the taskbar and in Explorer. "
            + "LizTerm.parcel's Win32Settings.InstallerIcon does not cover this: it is the installer's own icon.");
    }

    [Fact]
    public void The_declared_icon_exists_on_disk_and_is_an_ico()
    {
        var declared = Declared();
        Assert.NotNull(declared);

        // ApplicationIcon resolves against the project directory, so that is where this has to look -- a path
        // that is correct relative to the repository root would build an icon-less executable in silence.
        var project = Path.GetDirectoryName(ProjectPath())!;
        var path = Path.Combine(project, declared.Replace('\\', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(path),
            $"<ApplicationIcon> names '{declared}', which does not exist relative to src/LizTerm.App. "
            + "The SDK treats a missing icon as an error, so this would fail the build rather than ship -- "
            + "but it would fail it in the Windows publish job, minutes into a release run.");
        Assert.True(Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase),
            $"<ApplicationIcon> names '{declared}'. Windows needs an .ico; a .png is not a Win32 icon resource.");
    }

    /// <summary>The icon is deliberately excluded from the Avalonia resource bundle to keep it out of the
    /// assembly, which is a separate mechanism from the Win32 resource the shell reads. Declaring the icon
    /// must not be mistaken later for a reason to embed it twice.</summary>
    [Fact]
    public void The_icon_stays_out_of_the_avalonia_resource_bundle()
    {
        var removed = XDocument.Load(ProjectPath())
            .Descendants("AvaloniaResource")
            .Select(e => e.Attribute("Remove")?.Value)
            .Where(v => v is not null)
            .Select(v => v!.Replace('\\', '/'))
            .ToList();

        Assert.Contains("Assets/Icons/lizterm.ico", removed);
    }
}
