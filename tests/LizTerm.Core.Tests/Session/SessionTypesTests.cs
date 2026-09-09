// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Session;

public class SessionTypesTests
{
    [Theory]
    [InlineData(ConnectionState.Disconnected, false)]
    [InlineData(ConnectionState.TcpPending, false)]
    [InlineData(ConnectionState.TelnetPending, false)]
    [InlineData(ConnectionState.ConnectedNvt, true)]
    [InlineData(ConnectionState.Connected3270, true)]
    [InlineData(ConnectionState.ConnectedTn3270E, true)]
    public void IsConnected_is_true_only_for_connected_states(ConnectionState state, bool expected) =>
        Assert.Equal(expected, state.IsConnected());

    [Fact]
    public void PF_keys_are_contiguous_so_arithmetic_works()
    {
        Assert.Equal(TerminalKey.PF12, TerminalKey.PF1 + 11);
        Assert.Equal(TerminalKey.PF24, TerminalKey.PF1 + 23);
    }

    [Fact]
    public void Profile_defaults_match_spec()
    {
        var p = new SessionProfile { Name = "x", Host = "h" };
        Assert.Equal(23, p.Port);
        Assert.False(p.UseTls);
        Assert.True(p.VerifyCertificate);
        Assert.Equal(2, p.Model);
        Assert.True(p.Extended);
        Assert.Equal("cp037", p.CodePage);
        Assert.Null(p.LuName);
    }

    [Fact]
    public void Initial_keyboard_status_is_not_connected() =>
        Assert.Equal(KeyboardLock.NotConnected, KeyboardStatus.Initial.Lock);

    [Fact]
    public void Backspace_erases_by_default_and_a_new_profile_has_no_pin()
    {
        var profile = new SessionProfile();
        Assert.True(profile.DestructiveBackspace);
        Assert.Null(profile.PinnedCertificate);
    }
}
