// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Session;

/// <summary>Host session states as b3270 reports them. The declaration order is NOT b3270's progression order:
/// the recorded fixtures go tcp-pending, telnet-pending, tls-pending, and back to telnet-pending before the
/// session comes up. Group these with the helpers below rather than comparing one against a point in the list.</summary>
public enum ConnectionState
{
    Disconnected,
    Reconnecting,
    Resolving,
    TcpPending,
    TlsPending,
    TlsPasswordPending,
    ProxyPending,
    TelnetPending,
    ConnectedNvt,
    ConnectedNvtCharMode,
    Connected3270,
    ConnectedUnbound,
    ConnectedENvt,
    ConnectedSscp,
    ConnectedTn3270E,
}

public static class ConnectionStateExtensions
{
    /// <summary>A 3270 session is up. Safe as a comparison because every Connected member is declared last,
    /// so this is a grouping test rather than a claim about the order b3270 reaches them in.</summary>
    public static bool IsConnected(this ConnectionState state) => state >= ConnectionState.ConnectedNvt;

    /// <summary>b3270 has a socket to the host: past name resolution and the TCP connect, whether or not the
    /// 3270 session has come up. Spelled out rather than compared, because the states past the connect are not
    /// contiguous in this list.</summary>
    public static bool HasSocket(this ConnectionState state) =>
        state is not (ConnectionState.Disconnected or ConnectionState.Reconnecting
            or ConnectionState.Resolving or ConnectionState.TcpPending);
}
