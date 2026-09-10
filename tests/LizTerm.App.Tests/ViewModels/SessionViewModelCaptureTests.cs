// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Files;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class SessionViewModelCaptureTests
{
    private static (SessionViewModel Vm, FakeEmulatorSession Session, FakeTextClipboard Clipboard) Build()
    {
        var session = new FakeEmulatorSession();
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(2, 3, "READY", HostColor.Green, null, null);
        session.CurrentScreen = buffer.Snapshot();
        var clipboard = new FakeTextClipboard();
        return (new SessionViewModel(session, action => action(), clipboard), session, clipboard);
    }

    [Fact]
    public void The_suggested_name_carries_the_profile_the_stamp_and_the_extension()
    {
        var name = SessionViewModel.ScreenFileName("TK5", new DateTime(2026, 9, 9, 14, 22, 33), "txt");

        Assert.Equal("screen-TK5-20260909-142233.txt", name);
    }

    [Fact]
    public void A_profile_name_with_path_characters_is_made_safe()
    {
        var name = SessionViewModel.ScreenFileName("a/b c", new DateTime(2026, 9, 9, 1, 2, 3), "html");

        Assert.Equal("screen-a_b_c-20260909-010203.html", name);
    }

    [Fact]
    public async Task Saving_writes_plain_text_for_a_txt_path()
    {
        var (vm, _, _) = Build();
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
        var picker = new FakeFilePicker { Result = path };

        await vm.SaveScreenAsync(picker);

        var written = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Contains("READY", written);
        Assert.DoesNotContain("<span", written);
        File.Delete(path);
    }

    /// <summary>The format is still decided by the extension, but the OS dialog now carries its own File Format
    /// popup so the choice is visible and the extension is appended for you. Before this, typing ".html" by
    /// hand was the only route to an HTML capture and nothing in the UI said so.</summary>
    [Fact]
    public async Task The_save_dialog_offers_text_and_html_as_formats()
    {
        var (vm, _, _) = Build();
        var picker = new FakeFilePicker { Result = null };

        await vm.SaveScreenAsync(picker);

        Assert.NotNull(picker.LastSaveFormats);
        Assert.Equal(["txt", "html"], picker.LastSaveFormats!.Select(f => f.Extension));
        Assert.All(picker.LastSaveFormats, f => Assert.False(string.IsNullOrWhiteSpace(f.Label)));
    }

    /// <summary>The first choice is what the dialog opens on, so it has to be the one the suggested file name
    /// already ends in — otherwise the dialog contradicts its own filename the moment it opens.</summary>
    [Fact]
    public async Task The_first_offered_format_matches_the_suggested_file_name()
    {
        var (vm, _, _) = Build();
        var picker = new FakeFilePicker { Result = null };

        await vm.SaveScreenAsync(picker);

        Assert.Equal("txt", picker.LastSaveFormats![0].Extension);
        Assert.EndsWith(".txt", Assert.Single(picker.Calls));
    }

    /// <summary>A received file can be anything, so the transfer dialog offers no format list at all.</summary>
    [Fact]
    public async Task A_transfer_save_offers_no_formats()
    {
        var picker = new FakeFilePicker { Result = null };

        await picker.PickSaveLocationAsync("REPORT.TXT", "Save received file as");

        Assert.Null(picker.LastSaveFormats);
    }

    [Fact]
    public async Task Saving_writes_html_for_an_html_path()
    {
        var (vm, _, _) = Build();
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".html");
        var picker = new FakeFilePicker { Result = path };

        await vm.SaveScreenAsync(picker);

        var written = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Contains("<span", written);
        Assert.Contains("READY", written);
        File.Delete(path);
    }

    /// <summary>The saved file, unlike the clipboard, has no surrounding document to inherit an encoding from:
    /// opened from disk as `file://` with no charset declared, a browser falls back to its locale default, and
    /// LizTerm's own keymap types characters outside ASCII (`¬` on Ctrl+[, `¢` on Ctrl+6).</summary>
    [Fact]
    public async Task Saving_html_declares_utf8_so_a_reopened_file_is_not_mojibake()
    {
        var session = new FakeEmulatorSession();
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(0, 0, "¬", null, null, null); // what Ctrl+[ types
        session.CurrentScreen = buffer.Snapshot();
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard());
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".html");
        var picker = new FakeFilePicker { Result = path };

        await vm.SaveScreenAsync(picker);

        var written = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Contains("<meta charset=\"utf-8\">", written);
        Assert.Contains('¬', written);
        File.Delete(path);
    }

    /// <summary>Only `.txt` and `.html` were ever exercised; `.htm`, an unrecognised extension, no extension at
    /// all, and an upper-case `.HTML` all fall through the same branch and were untested.</summary>
    [Theory]
    [InlineData(".htm", true)]
    [InlineData(".HTML", true)]
    [InlineData(".log", false)]
    [InlineData("", false)]
    public async Task Saving_chooses_the_format_from_the_extension_case_insensitively(string extension, bool expectHtml)
    {
        var (vm, _, _) = Build();
        var path = Path.Combine(Path.GetTempPath(), "lizterm-capture-" + Guid.NewGuid().ToString("N") + extension);
        var picker = new FakeFilePicker { Result = path };

        await vm.SaveScreenAsync(picker);

        var written = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        if (expectHtml)
        {
            Assert.Contains("<span", written);
        }
        else
        {
            Assert.DoesNotContain("<span", written);
            Assert.Contains("READY", written);
        }
        File.Delete(path);
    }

    [Fact]
    public async Task A_cancelled_save_dialog_writes_nothing_and_reports_nothing()
    {
        var (vm, _, _) = Build();
        var picker = new FakeFilePicker { Result = null };

        await vm.SaveScreenAsync(picker);

        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task A_failing_save_reports_through_the_error_banner()
    {
        var (vm, _, _) = Build();
        var picker = new FakeFilePicker { Result = Path.Combine(Path.GetTempPath(), "no-such-dir", "x.txt") };

        await vm.SaveScreenAsync(picker);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("Could not save the screen", vm.ErrorMessage);
    }

    [Fact]
    public async Task Copying_puts_html_on_the_clipboard()
    {
        var (vm, _, clipboard) = Build();

        await vm.CopyScreenAsHtmlAsync();

        Assert.NotNull(clipboard.Text);
        Assert.Contains("<span", clipboard.Text);
        Assert.Contains("READY", clipboard.Text);
    }

    /// <summary>The whole reason capture does not go through b3270: the moment you most want to keep a screen
    /// is often one the host has just dropped.</summary>
    [Fact]
    public async Task Capture_works_while_disconnected()
    {
        var (vm, session, clipboard) = Build();
        session.RaiseConnection(ConnectionState.Disconnected);

        Assert.False(vm.IsConnected);
        Assert.True(vm.CanCaptureScreen);

        await vm.CopyScreenAsHtmlAsync();
        Assert.NotNull(clipboard.Text);
    }
}
