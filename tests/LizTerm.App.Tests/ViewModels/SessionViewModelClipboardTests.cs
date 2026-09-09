// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class SessionViewModelClipboardTests
{
    private static (SessionViewModel Vm, FakeEmulatorSession Session, FakeTextClipboard Clipboard) Create()
    {
        var session = new FakeEmulatorSession();
        var clipboard = new FakeTextClipboard();
        return (new SessionViewModel(session, action => action(), clipboard), session, clipboard);
    }

    private static ScreenSnapshot ScreenWith(string text, int row = 2, int column = 5)
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetText(row, column, text, null, null, null);
        return buffer.Snapshot();
    }

    [Fact]
    public async Task Copy_writes_the_trimmed_region_and_keeps_the_selection()
    {
        var (vm, session, clipboard) = Create();
        session.RaiseScreen(ScreenWith("hello   "));
        vm.Selection = ScreenRegion.FromCorners(2, 5, 3, 14);

        Assert.True(vm.CopyCommand.CanExecute(null));
        await vm.CopyCommand.ExecuteAsync(null);

        Assert.Equal("hello\n", clipboard.Text);
        Assert.Equal(ScreenRegion.FromCorners(2, 5, 3, 14), vm.Selection);
        Assert.Empty(session.Calls);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void Copy_is_enabled_only_with_a_selection()
    {
        var (vm, _, _) = Create();
        Assert.False(vm.CopyCommand.CanExecute(null));
        vm.Selection = ScreenRegion.FromCorners(0, 0, 0, 0);
        Assert.True(vm.CopyCommand.CanExecute(null));
        vm.Selection = null;
        Assert.False(vm.CopyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Paste_normalizes_newlines_sends_one_paste_and_clears_the_selection()
    {
        var (vm, session, clipboard) = Create();
        session.RaiseConnection(ConnectionState.Connected3270);
        vm.Selection = ScreenRegion.FromCorners(0, 0, 0, 0);
        clipboard.Text = "line one\r\nline two\rline three";

        Assert.True(vm.PasteCommand.CanExecute(null));
        await vm.PasteCommand.ExecuteAsync(null);

        Assert.Equal(["paste:line one\nline two\nline three"], session.Calls);
        Assert.Null(vm.Selection);
    }

    [Fact]
    public async Task Paste_is_disabled_while_disconnected_and_sends_nothing_when_the_clipboard_is_empty()
    {
        var (vm, session, clipboard) = Create();
        Assert.False(vm.PasteCommand.CanExecute(null));

        session.RaiseConnection(ConnectionState.Connected3270);
        Assert.True(vm.PasteCommand.CanExecute(null));
        clipboard.Text = null;
        await vm.PasteCommand.ExecuteAsync(null);
        clipboard.Text = "";
        await vm.PasteCommand.ExecuteAsync(null);

        Assert.Empty(session.Calls);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void SelectAll_covers_the_current_screen()
    {
        var (vm, session, _) = Create();
        Assert.True(vm.SelectAllCommand.CanExecute(null));
        session.RaiseScreen(ScreenSnapshot.Empty(43, 80));
        vm.SelectAllCommand.Execute(null);
        Assert.Equal(ScreenRegion.Full(43, 80), vm.Selection);
    }

    [Fact]
    public async Task Input_sent_to_the_host_clears_the_selection()
    {
        var (vm, _, _) = Create();

        vm.Selection = ScreenRegion.FromCorners(1, 1, 2, 2);
        await vm.SendKeyCommand.ExecuteAsync(TerminalKey.Enter);
        Assert.Null(vm.Selection);

        vm.Selection = ScreenRegion.FromCorners(1, 1, 2, 2);
        await vm.TypeTextAsync("x");
        Assert.Null(vm.Selection);

        vm.Selection = ScreenRegion.FromCorners(1, 1, 2, 2);
        await vm.MoveCursorAsync(3, 3);
        Assert.Null(vm.Selection);
    }

    [Fact]
    public async Task Clipboard_failures_show_a_plain_message()
    {
        var (vm, session, clipboard) = Create();
        session.RaiseConnection(ConnectionState.Connected3270);
        vm.Selection = ScreenRegion.FromCorners(0, 0, 0, 0);
        clipboard.Exception = new InvalidOperationException("clipboard busy");

        await vm.CopyCommand.ExecuteAsync(null);
        Assert.Equal("Could not copy: clipboard busy", vm.ErrorMessage);

        await vm.PasteCommand.ExecuteAsync(null);
        Assert.Equal("Could not read the clipboard: clipboard busy", vm.ErrorMessage);
        Assert.Empty(session.Calls);
    }

    [Fact]
    public async Task Paste_executed_directly_while_disconnected_does_nothing()
    {
        var (vm, session, clipboard) = Create();
        clipboard.Text = "text";
        vm.Selection = ScreenRegion.FromCorners(0, 0, 0, 0);

        await vm.PasteCommand.ExecuteAsync(null);

        Assert.Empty(session.Calls);
        Assert.Equal(ScreenRegion.FromCorners(0, 0, 0, 0), vm.Selection);
        Assert.Null(vm.ErrorMessage);
    }
}
