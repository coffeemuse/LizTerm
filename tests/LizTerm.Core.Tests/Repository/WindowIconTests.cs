// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LizTerm.Core.Tests.Repository;

/// <summary>Every window's icon is set once, by a style in App.axaml, to an unlettered multi-size .ico: a title bar
/// or taskbar draws it at 16 to 32 px, where the lettered lizterm-256.png every window used to name is a green
/// smear. A window that sets Icon itself beats the style in silence, so this holds both halves in place.</summary>
public class WindowIconTests
{
    private static string AppProject() => Path.Combine(LicenseHeader.Root(), "src", "LizTerm.App");

    // An Icon attribute on its own, not NoteIcon= or a property element such as MenuItem.Icon.
    private static readonly Regex IconAttribute = new(@"(?<![\w.:])Icon\s*=\s*""", RegexOptions.Compiled);

    [Fact]
    public void No_window_sets_its_own_icon()
    {
        var project = AppProject();
        var offenders = Directory.EnumerateFiles(project, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => IconAttribute.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(project, f))
            .ToList();

        Assert.True(offenders.Count == 0,
            "These set Icon themselves, which overrides App.axaml's window icon style: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void The_app_style_names_an_embedded_ico()
    {
        var project = AppProject();
        XNamespace avalonia = "https://github.com/avaloniaui";
        var value = XDocument.Load(Path.Combine(project, "App.axaml"))
            .Descendants(avalonia + "Setter")
            .Where(s => (string?)s.Attribute("Property") == "Icon")
            .Select(s => (string?)s.Attribute("Value"))
            .SingleOrDefault();

        const string prefix = "avares://LizTerm.App/";
        Assert.NotNull(value);
        Assert.StartsWith(prefix, value);
        var relative = value[prefix.Length..];
        Assert.EndsWith(".ico", relative);
        Assert.True(File.Exists(Path.Combine(project, relative)), $"App.axaml's window icon {value} does not exist.");

        var removed = XDocument.Load(Path.Combine(project, "LizTerm.App.csproj"))
            .Descendants("AvaloniaResource")
            .Select(e => e.Attribute("Remove")?.Value?.Replace('\\', '/'));
        Assert.DoesNotContain(relative, removed);
    }
}
