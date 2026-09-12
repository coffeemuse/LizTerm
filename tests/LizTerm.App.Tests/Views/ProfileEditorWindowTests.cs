// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LizTerm.App.Views;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Views;

public class ProfileEditorWindowTests
{
    [AvaloniaFact]
    public void The_pinned_line_shows_only_for_a_pinned_profile_and_forget_hides_it()
    {
        var pinned = new ProfileEditorWindow(new SessionProfile
        {
            Name = "gw", Host = "gw", UseTls = true, PinnedCertificate = new CertificatePin("8C:13", "CN=gw", "pem"),
        });
        pinned.Show();
        var panel = pinned.FindControl<StackPanel>("PinPanel")!;
        Assert.True(panel.IsVisible);
        Assert.Equal("Pinned certificate: SHA-256 8C:13 (CN=gw)", pinned.FindControl<TextBlock>("PinText")!.Text);
        pinned.FindControl<Button>("ForgetButton")!.Command!.Execute(null);
        Assert.False(panel.IsVisible);

        var plain = new ProfileEditorWindow(new SessionProfile { Name = "p", Host = "h" });
        plain.Show();
        Assert.False(plain.FindControl<StackPanel>("PinPanel")!.IsVisible);
        Assert.Equal("Backspace erases the previous character (off: Backspace only moves the cursor left)",
            plain.FindControl<CheckBox>("BackspaceBox")!.Content);
    }

    [AvaloniaFact]
    public void The_tag_and_note_rows_render_the_profiles_values()
    {
        var window = new ProfileEditorWindow(new SessionProfile
        {
            Name = "mvsce", Host = "h", Tags = TagSet.From(["FAVORITE", "PROD"]), Note = "no live data",
        });
        window.Show();

        Assert.Equal("PROD", window.FindControl<TextBox>("TagsBox")!.Text);
        Assert.True(window.FindControl<CheckBox>("FavoriteBox")!.IsChecked);
        Assert.Equal("no live data", window.FindControl<TextBox>("NoteBox")!.Text);
    }

    /// <summary>A pasted paragraph must not reach the list, where it would reshape every row.</summary>
    [AvaloniaFact]
    public void The_note_box_is_single_line_and_capped()
    {
        var window = new ProfileEditorWindow(new SessionProfile { Name = "p", Host = "h" });
        window.Show();
        var note = window.FindControl<TextBox>("NoteBox")!;
        Assert.False(note.AcceptsReturn);
        Assert.Equal(120, note.MaxLength);
    }
}
