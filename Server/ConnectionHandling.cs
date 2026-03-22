using System.Buffers;
using CentrED.Network;

namespace CentrED.Server;

/// <summary>
/// Handles connection-level packets such as login, quit, and keep-alive traffic.
/// </summary>
public class ConnectionHandling
{
    // The connection packet family uses a one-byte subcommand identifier, so a
    // fixed lookup table keeps dispatch predictable and allocation-free.
    private static PacketHandler<CEDServer>?[] Handlers { get; }

    static ConnectionHandling()
    {
        Handlers = new PacketHandler<CEDServer>?[0x100];

        Handlers[0x03] = new PacketHandler<CEDServer>(0, OnLoginRequestPacket);
        Handlers[0x05] = new PacketHandler<CEDServer>(0, OnQuitPacket);
    }

    /// <summary>
    /// Dispatches a connection-management packet to its registered sub-handler.
    /// </summary>
    /// <param name="reader">The packet payload reader positioned after the outer packet header.</param>
    /// <param name="ns">The client session that sent the packet.</param>
    public static void OnConnectionHandlerPacket(SpanReader reader, NetState<CEDServer> ns)
    {
        ns.LogDebug("Server OnConnectionHandlerPacket");
        var id = reader.ReadByte();
        var packetHandler = Handlers[id];
        packetHandler?.OnReceive(reader, ns);
    }

    /// <summary>
    /// Validates login credentials and sends the initial post-login synchronization packets.
    /// </summary>
    /// <param name="reader">The payload reader containing the username and password.</param>
    /// <param name="ns">The client session requesting authentication.</param>
    private static void OnLoginRequestPacket(SpanReader reader, NetState<CEDServer> ns)
    {
        ns.LogDebug("Server OnLoginRequestPacket");
        var username = reader.ReadString();
        var password = reader.ReadString();
        var account = ns.Parent.GetAccount(username);
        if (account == null)
        {
            ns.LogDebug($"Invalid account specified: {username}");
            ns.Send(new LoginResponsePacket(LoginState.InvalidUser));
            ns.Disconnect();
        }
        else if (account.AccessLevel <= AccessLevel.None)
        {
            ns.LogDebug("Access Denied");
            ns.Send(new LoginResponsePacket(LoginState.NoAccess));
            ns.Disconnect();
        }
        else if (!account.CheckPassword(password))
        {
            ns.LogDebug("Invalid password");
            ns.Send(new LoginResponsePacket(LoginState.InvalidPassword));
            ns.Disconnect();
        }
        else if (ns.Parent.Clients.Any(client => client.Username == account.Name))
        {
            ns.Send(new LoginResponsePacket(LoginState.AlreadyLoggedIn));
            ns.Disconnect();
        }
        else
        {
            ns.LogInfo($"Login {username}");
            ns.Username = account.Name;
            ns.Send(new LoginResponsePacket(LoginState.Ok, ns));
            ns.SendCompressed(new ClientListPacket(ns));
            ns.Parent.Broadcast(new ClientConnectedPacket(ns));
            ns.Send(new SetClientPosPacket(ns));
        }
    }

    /// <summary>
    /// Acknowledges a client disconnect request and terminates the session.
    /// </summary>
    /// <param name="reader">The payload reader.</param>
    /// <param name="ns">The client session requesting disconnect.</param>
    private static void OnQuitPacket(SpanReader reader, NetState<CEDServer> ns)
    {
        ns.LogDebug("Server OnQuitPacket");
        ns.Send(new QuitAckPacket());
        ns.Disconnect();
    }
    
    /// <summary>
    /// Handles the protocol no-op packet used to keep a session alive.
    /// </summary>
    /// <param name="buffer">The payload reader.</param>
    /// <param name="ns">The client session that sent the keep-alive packet.</param>
    public static void OnNoOpPacket(SpanReader buffer, NetState<CEDServer> ns)
    {
        ns.LogDebug("Server OnNoOpPacket");
    }
}