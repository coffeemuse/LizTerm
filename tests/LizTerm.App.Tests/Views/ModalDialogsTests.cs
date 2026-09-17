// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Dialogs;

namespace LizTerm.App.Tests.Views;

/// <summary>A dialog over a Keep on Top window must be Keep on Top too: on macOS the owner sits at the floating
/// window level and a normal-level dialog draws beneath it, out of reach while the owner refuses input.</summary>
public class ModalDialogsTests
{
    private static Window ShownOwner(bool topmost)
    {
        var owner = new Window { Topmost = topmost };
        owner.Show();
        return owner;
    }

    [AvaloniaFact]
    public void A_dialog_over_a_keep_on_top_window_opens_keep_on_top()
    {
        var owner = ShownOwner(topmost: true);
        var dialog = new Window();

        _ = dialog.ShowDialogAbove(owner);

        Assert.Same(dialog, Assert.Single(owner.OwnedWindows));
        Assert.True(dialog.Topmost);
        dialog.Close();
    }

    [AvaloniaFact]
    public void A_dialog_over_an_ordinary_window_stays_ordinary()
    {
        var owner = ShownOwner(topmost: false);
        var dialog = new Window();

        _ = dialog.ShowDialogAbove(owner);

        Assert.False(dialog.Topmost);
        dialog.Close();
    }

    /// <summary>The macOS menu bar stays live over a modal dialog, so Window > Keep on Top can still flip the owner.</summary>
    [AvaloniaFact]
    public void An_open_dialog_follows_its_owner_in_and_out_of_keep_on_top()
    {
        var owner = ShownOwner(topmost: false);
        var dialog = new Window();
        _ = dialog.ShowDialogAbove(owner);

        owner.Topmost = true;
        Assert.True(dialog.Topmost);
        owner.Topmost = false;
        Assert.False(dialog.Topmost);
        dialog.Close();
    }

    [AvaloniaFact]
    public async Task A_closed_dialog_no_longer_follows_its_owner()
    {
        var owner = ShownOwner(topmost: true);
        var dialog = new Window();
        var shown = dialog.ShowDialogAbove(owner);

        dialog.Close();
        await shown;
        owner.Topmost = false;

        Assert.True(dialog.Topmost);
    }

    [AvaloniaFact]
    public async Task The_dialog_result_comes_back()
    {
        var owner = ShownOwner(topmost: true);
        var dialog = new Window();
        var shown = dialog.ShowDialogAbove<string?>(owner);

        dialog.Close("chosen");

        Assert.Equal("chosen", await shown);
    }

    /// <summary>The rule only holds if nothing opens a dialog around it. ModalDialogs is the one file allowed to
    /// call ShowDialog itself.</summary>
    [Fact]
    public void Every_dialog_in_the_app_opens_through_ShowDialogAbove()
    {
        var source = Path.Combine(RepositoryRoot(), "src", "LizTerm.App");
        var files = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(source, f).Split(Path.DirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
            .ToList();
        Assert.NotEmpty(files);

        var offenders = files
            .Where(f => Path.GetFileName(f) != "ModalDialogs.cs")
            .Where(f => File.ReadAllText(f).Contains(".ShowDialog(", StringComparison.Ordinal)
                || File.ReadAllText(f).Contains(".ShowDialog<", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(source, f))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These files call ShowDialog directly; use ShowDialogAbove so the dialog keeps above a Keep on Top owner:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [AvaloniaFact]
    public void An_owned_window_takes_and_follows_its_owners_keep_on_top_until_it_closes()
    {
        var owner = new Window { Topmost = true };
        owner.Show();
        var child = new Window();

        child.ShowAbove(owner);

        Assert.Same(owner, child.Owner);
        Assert.Contains(child, owner.OwnedWindows);
        Assert.True(child.Topmost);
        owner.Topmost = false;
        Assert.False(child.Topmost);

        child.Close();
        owner.Topmost = true;
        Assert.False(child.Topmost);
        owner.Close();
    }

    [AvaloniaFact]
    public void A_window_that_could_not_be_shown_does_not_follow_the_owner()
    {
        var owner = new Window();
        owner.Show();
        owner.Close();
        var child = new Window();

        Assert.ThrowsAny<Exception>(() => child.ShowAbove(owner));

        owner.Topmost = true;
        Assert.False(child.Topmost);
    }

    [AvaloniaFact]
    public void An_owned_window_closes_with_its_owner()
    {
        var owner = new Window();
        owner.Show();
        var child = new Window();
        var closed = false;
        child.Closed += (_, _) => closed = true;
        child.ShowAbove(owner);

        owner.Close();

        Assert.True(closed);
    }

    /// <summary>The repository, found from this file's compile-time path as OiaGlyphsTests does.</summary>
    private static string RepositoryRoot([CallerFilePath] string path = "")
    {
        var dir = Path.GetDirectoryName(path);
        while (dir is not null && !File.Exists(Path.Combine(dir, "LizTerm.slnx"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException($"No LizTerm.slnx above '{path}'.");
    }
}
