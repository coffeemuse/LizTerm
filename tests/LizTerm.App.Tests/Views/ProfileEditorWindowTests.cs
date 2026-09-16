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

    [AvaloniaFact]
    public void The_mvsmf_group_shows_the_profiles_values_and_its_pin()
    {
        var window = new ProfileEditorWindow(new SessionProfile
        {
            Name = "MVS/CE", Host = "mvs", HostFilesUrl = "http://mvs:8080/zosmf", HostFilesUserid = "MVSCE02",
            HostFilesPinnedCertificate = new CertificatePin("AA:BB", "CN=proxy", "pem"),
        });
        window.Show();

        Assert.Equal("http://mvs:8080/zosmf", window.FindControl<TextBox>("MvsmfUrlBox")!.Text);
        Assert.Equal("MVSCE02", window.FindControl<TextBox>("MvsmfUseridBox")!.Text);
        Assert.True(window.FindControl<StackPanel>("MvsmfPinPanel")!.IsVisible);
        Assert.Equal("Pinned certificate: SHA-256 AA:BB (CN=proxy)", window.FindControl<TextBlock>("MvsmfPinText")!.Text);
        Assert.True(window.FindControl<Button>("MvsmfTestButton")!.IsEffectivelyEnabled);
        window.FindControl<Button>("MvsmfForgetButton")!.Command!.Execute(null);
        Assert.False(window.FindControl<StackPanel>("MvsmfPinPanel")!.IsVisible);
    }

    [AvaloniaFact]
    public void A_profile_without_mvsmf_shows_an_empty_group()
    {
        var window = new ProfileEditorWindow(new SessionProfile { Name = "p", Host = "h" });
        window.Show();

        Assert.Equal("", window.FindControl<TextBox>("MvsmfUrlBox")!.Text);
        Assert.False(window.FindControl<StackPanel>("MvsmfPinPanel")!.IsVisible);
    }
}
