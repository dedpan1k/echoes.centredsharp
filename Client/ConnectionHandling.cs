using System.Buffers;
using CentrED.Network;

namespace CentrED.Client;

/// <summary>
/// Handles connection-level packets such as protocol negotiation, login, server state, and quit acknowledgements.
/// </summary>
public static class ConnectionHandling
{
    // Connection packets use one-byte subcommand identifiers, so a lookup table keeps dispatch simple.
    private static PacketHandler<CentrEDClient>?[] Handlers { get; }

    static ConnectionHandling()
    {
        Handlers = new PacketHandler<CentrEDClient>?[0x100];

        Handlers[0x01] = new PacketHandler<CentrEDClient>(0, OnProtocolVersionPacket);
        Handlers[0x03] = new PacketHandler<CentrEDClient>(0, OnLoginResponsePacket);
        Handlers[0x04] = new PacketHandler<CentrEDClient>(0, OnServerStatePacket);
        Handlers[0x05] = new PacketHandler<CentrEDClient>(0, OnQuitAckPacket);
    }

    /// <summary>
    /// Dispatches an incoming connection-management packet.
    /// </summary>
    /// <param name="reader">The packet reader positioned after the outer header.</param>
    /// <param name="ns">The client network session.</param>
    public static void OnConnectionHandlerPacket(SpanReader reader, NetState<CentrEDClient> ns)
    {
        ns.LogDebug("Client OnConnectionHandlerPacket");
        var id = reader.ReadByte();
        var packetHandler = Handlers[id];
        packetHandler?.OnReceive(reader, ns);
    }

    /// <summary>
    /// Negotiates the protocol version advertised by the server.
    /// </summary>
    /// <param name="reader">The packet payload reader.</param>
    /// <param name="ns">The client network session.</param>
    private static void OnProtocolVersionPacket(SpanReader reader, NetState<CentrEDClient> ns)
    {
        ns.LogDebug("Client OnProtocolVersionPacket");
        var version = reader.ReadUInt32();
        ns.ProtocolVersion = (ProtocolVersion)version switch
        {
            ProtocolVersion.CentrED => ProtocolVersion.CentrED,
            ProtocolVersion.CentrEDPlus => ProtocolVersion.CentrEDPlus,
            _ => throw new ArgumentException($"Unsupported protocol version {version}")
        };
    }

    /// <summary>
    /// Processes the login response and initializes the landscape on success.
    /// </summary>
    /// <param name="reader">The packet payload reader.</param>
    /// <param name="ns">The client network session.</param>
    private static void OnLoginResponsePacket(SpanReader reader, NetState<CentrEDClient> ns)
    {
        ns.LogDebug("Client OnLoginResponsePacket");
        var loginState = (LoginState)reader.ReadByte();
        string logMessage;
        switch (loginState)
        {
            case LoginState.Ok:
                ns.LogInfo("Initializing");
                ns.Parent.AccessLevel = (AccessLevel)reader.ReadByte();
                if (ns.ProtocolVersion == ProtocolVersion.CentrEDPlus)
                {
                    reader.ReadUInt32(); // server uptime
                }
                var width = reader.ReadUInt16();
                var height = reader.ReadUInt16();
                if (ns.ProtocolVersion == ProtocolVersion.CentrEDPlus)
                {
                    reader.ReadUInt32(); // flags
                }

                ns.Parent.InitLandscape(width, height);
                ClientHandling.ReadAccountRestrictions(reader);
                logMessage = "Connected";
                break;
            case LoginState.InvalidUser:
                logMessage = "The username you specified is incorrect.";
                ns.Parent.Shutdown();
                break;
            case LoginState.InvalidPassword:
                logMessage = "The password you specified is incorrect.";
                ns.Parent.Shutdown();
                break;
            case LoginState.AlreadyLoggedIn:
                logMessage = "There is already a client logged in using that account.";
                ns.Parent.Shutdown();
                break;
            case LoginState.NoAccess:
                logMessage = "This account has no access.";
                ns.Parent.Shutdown();
                break;
            default: throw new ArgumentException($"Unknown login state{loginState}");
        }
        ns.Parent.Status = logMessage;
        if (ns.Parent.Running)
        {
            ns.LogInfo(logMessage);
        }
        else
        {
            ns.LogError(logMessage);
        }
    }

    /// <summary>
    /// Updates the cached coarse-grained server state.
    /// </summary>
    /// <param name="reader">The packet payload reader.</param>
    /// <param name="ns">The client network session.</param>
    private static void OnServerStatePacket(SpanReader reader, NetState<CentrEDClient> ns)
    {
        ns.Parent.ServerState = (ServerState)reader.ReadByte();
        if (ns.Parent.ServerState == ServerState.Other)
        {
            ns.Parent.ServerStateReason = reader.ReadString();
        }
    }
    
    /// <summary>
    /// Handles the server's quit acknowledgement.
    /// </summary>
    /// <param name="reader">The packet payload reader.</param>
    /// <param name="ns">The client network session.</param>
    private static void OnQuitAckPacket(SpanReader reader, NetState<CentrEDClient> ns)
    {
        ns.LogDebug("Client OnQuitAckPacket");
        ns.Parent.Shutdown();
    }
}