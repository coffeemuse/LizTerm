// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class SessionViewModelTests
{
    private static (SessionViewModel Vm, FakeEmulatorSession Session) Create()
    {
        var session = new FakeEmulatorSession();
        return (new SessionViewModel(session, action => action(), new FakeTextClipboard()), session);
    }

    [Fact]
    public void Initial_state_reflects_the_session()
    {
        var (vm, session) = Create();
        Assert.Equal("Fake - fake.host", vm.Title);
        Assert.Same(session.CurrentScreen, vm.Screen);
        Assert.Equal("Not connected", vm.ConnectionText);
        Assert.Equal("✕ Not connected", vm.KeyboardText);
        Assert.Equal("Model 2-E", vm.ModelText);
        Assert.False(vm.IsConnected);
    }

    [Fact]
    public void Screen_event_updates_screen_and_cursor_text()
    {
        var (vm, session) = Create();
        var buffer = new ScreenBuffer(24, 80);
        buffer.SetCursor(new CursorPosition(3, 9, true));
        var snapshot = buffer.Snapshot();
        session.RaiseScreen(snapshot);
        Assert.Same(snapshot, vm.Screen);
        Assert.Equal("04/010", vm.CursorText);
    }

    [Fact]
    public void Connection_event_updates_texts_and_flag()
    {
        var (vm, session) = Create();
        session.RaiseConnection(ConnectionState.ConnectedTn3270E, new TlsInfo(true, true, null, null));
        Assert.True(vm.IsConnected);
        Assert.Equal("Connected to fake.host (TN3270E)", vm.ConnectionText);
        Assert.Equal("\uE0A2 TLS, certificate verified", vm.TlsText);
    }

    [Fact]
    public void Status_event_updates_keyboard_insert_and_model()
    {
        var (vm, session) = Create();
        session.RaiseStatus(new KeyboardStatus(KeyboardLock.Unlocked, null, true, false, "LU01"));
        Assert.Equal("✓ Ready", vm.KeyboardText);
        Assert.Equal("INS", vm.InsertText);
        Assert.Equal("Model 2-E  LU LU01", vm.ModelText);
    }

    [Fact]
    public async Task Connect_command_calls_session_and_reports_failure()
    {
        var (vm, session) = Create();
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect"], session.Calls);
        Assert.Null(vm.ErrorMessage);

        session.ConnectException = new ConnectionFailedException(["Connection failed:", "Connection refused"]);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal("Connection failed: Connection refused", vm.ErrorMessage);
    }

    [Fact]
    public async Task Backend_unavailable_is_reported()
    {
        var (vm, session) = Create();
        session.ConnectException = new BackendUnavailableException("b3270 not found");
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal("b3270 not found", vm.ErrorMessage);
    }

    [Fact]
    public async Task Key_text_and_cursor_calls_are_forwarded()
    {
        var (vm, session) = Create();
        await vm.SendKeyCommand.ExecuteAsync(TerminalKey.PF3);
        await vm.TypeTextAsync("abc");
        await vm.MoveCursorAsync(2, 5);
        await vm.DisconnectCommand.ExecuteAsync(null);
        Assert.Equal(["key:PF3", "type:abc", "move:2,5", "disconnect"], session.Calls);
    }

    [Fact]
    public async Task Rejected_actions_are_swallowed_because_the_OIA_explains_them()
    {
        var (vm, session) = Create();
        session.ActionException = new EmulatorActionException("Keyboard locked");
        await vm.SendKeyCommand.ExecuteAsync(TerminalKey.Enter);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void Fault_and_host_messages_show_and_can_be_dismissed()
    {
        var (vm, session) = Create();
        session.RaiseHostMessage("Host unreachable");
        Assert.Equal("Host unreachable", vm.ErrorMessage);
        vm.DismissErrorCommand.Execute(null);
        Assert.Null(vm.ErrorMessage);
        session.RaiseFault(new BackendFault("b3270 died", [], 1));
        Assert.Contains("b3270 died", vm.ErrorMessage);
    }

    [Fact]
    public async Task Dispose_disposes_the_session()
    {
        var (vm, session) = Create();
        await vm.DisposeAsync();
        Assert.Contains("dispose", session.Calls);
    }

    [Fact]
    public async Task Events_after_dispose_do_not_change_state()
    {
        var (vm, session) = Create();
        await vm.DisposeAsync();

        session.RaiseHostMessage("late");
        session.RaiseConnection(ConnectionState.Connected3270);

        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsConnected);
    }

    [Fact]
    public async Task Unexpected_exception_is_reported()
    {
        var (vm, session) = Create();

        session.ActionException = new IOException("pipe broke");
        await vm.SendKeyCommand.ExecuteAsync(TerminalKey.Enter);
        Assert.Contains("pipe broke", vm.ErrorMessage);

        session.ConnectException = new IOException("boom");
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Contains("boom", vm.ErrorMessage);
    }
}
