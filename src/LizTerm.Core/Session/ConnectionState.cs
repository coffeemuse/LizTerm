namespace LizTerm.Core.Session;

/// <summary>Host session states, in the order b3270 progresses through them.</summary>
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
    public static bool IsConnected(this ConnectionState state) => state >= ConnectionState.ConnectedNvt;
}
