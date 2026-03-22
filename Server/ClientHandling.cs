using System.Buffers;
using CentrED.Network;
using CentrED.Server.Config;

namespace CentrED.Server;

/// <summary>
/// Handles client-facing collaboration packets such as chat, position updates, and password changes.
/// </summary>
public class ClientHandling
{
    // Client packet dispatch mirrors the protocol's one-byte subcommand layout.
    private static PacketHandler<CEDServer>?[] Handlers { get; }

    static ClientHandling()
    {
        Handlers = new PacketHandler<CEDServer>?[0x100];

        Handlers[0x04] = new PacketHandler<CEDServer>(0, OnUpdateClientPosPacket);
        Handlers[0x05] = new PacketHandler<CEDServer>(0, OnChatMessagePacket);
        Handlers[0x06] = new PacketHandler<CEDServer>(0, OnGotoClientPosPacket);
        Handlers[0x08] = new PacketHandler<CEDServer>(0, OnChangePasswordPacket);
    }

    /// <summary>
    /// Dispatches a client packet after verifying the sender has at least view access.
    /// </summary>
    /// <param name="reader">The packet payload reader positioned after the outer packet header.</param>
    /// <param name="ns">The client session that sent the packet.</param>
    public static void OnClientHandlerPacket(SpanReader reader, NetState<CEDServer> ns)
    {
        ns.LogDebug("Server OnClientHandlerPacket");
        if (!ns.ValidateAccess(AccessLevel.View))
            return;
        var packetHandler = Handlers[reader.ReadByte()];
        packetHandler?.OnReceive(reader, ns);
    }

    /// <summary>
    /// Updates the persisted last-known map position for the active client.
    /// </summary>
    /// <param name="reader">The payload reader containing the target position.</param>
    /// <param name="ns">The client session reporting its current position.</param>
    private static void OnUpdateClientPosPacket(SpanReader reader, NetState<CEDServer> ns)
    {
        ns.LogDebug("Server OnUpdateClientPosPacket");
        var x = reader.ReadUInt16();
        var y = reader.ReadUInt16();
        ns.Parent.GetAccount(ns.Username)!.LastPos = new LastPos(x, y);
        ns.Parent.Config.Invalidate();
    }

    /// <summary>
    /// Broadcasts a chat message from the sending client to all connected users.
    /// </summary>
    /// <param name="reader">The payload reader containing the message text.</param>
    /// <param name="ns">The client session that sent the message.</param>
    private static void OnChatMessagePacket(SpanReader reader, NetState<CEDServer> ns)
    {
        ns.LogDebug("Server OnChatMessagePacket");
        ns.Parent.Broadcast(new ChatMessagePacket(ns.Username, reader.ReadString()));
    }

    /// <summary>
    /// Moves the requesting client view to the last known location of another user.
    /// </summary>
    /// <param name="reader">The payload reader containing the target username.</param>
    /// <param name="ns">The client session requesting the jump.</param>
    private static void OnGotoClientPosPacket(SpanReader reader, NetState<CEDServer> ns)
    {
        ns.LogDebug("Server OnGotoClientPosPacket");
        var name = reader.ReadString();
        var client = ns.Parent.GetClient(name);
        if (client != null)
        {
            ns.Send(new SetClientPosPacket(client));
        }
    }

    /// <summary>
    /// Validates and applies a password change for the active account.
    /// </summary>
    /// <param name="reader">The payload reader containing the old and new passwords.</param>
    /// <param name="ns">The client session requesting the password update.</param>
    private static void OnChangePasswordPacket(SpanReader reader, NetState<CEDServer> ns)
    {
        ns.LogDebug("Server OnChangePasswordPacket");
        var oldPwd = reader.ReadString();
        var newPwd = reader.ReadString();
        var account = ns.Parent.GetAccount(ns.Username);
        if (account == null)
            return;

        PasswordChangeStatus status;
        if (!account.CheckPassword(oldPwd))
        {
            status = PasswordChangeStatus.OldPwInvalid;
        }
        else if (oldPwd == newPwd)
        {
            status = PasswordChangeStatus.Identical;
        }
        else if (newPwd.Length < 4)
        {
            status = PasswordChangeStatus.NewPwInvalid;
        }
        else
        {
            status = PasswordChangeStatus.Success;
            account.UpdatePassword(newPwd);
        }
        ns.Parent.Config.Invalidate();
        ns.Send(new PasswordChangeStatusPacket(status));
    }

    /// <summary>
    /// Writes the region restrictions that limit the active client's editable area.
    /// </summary>
    /// <param name="writer">The packet writer that receives the restriction payload.</param>
    /// <param name="ns">The client session whose account restrictions should be serialized.</param>
    public static void WriteAccountRestrictions(BinaryWriter writer, NetState<CEDServer> ns)
    {
        var account = ns.Parent.GetAccount(ns)!;
        if (account.AccessLevel >= AccessLevel.Administrator)
        {
            // The client still expects an area-count field even when unrestricted.
            writer.Write((ushort)0); //Client expects areaCount all the time
            return;
        }

        var rects = new List<RectU16>();
        foreach (var regionName in account.Regions)
        {
            var region = ns.Parent.GetRegion(regionName);
            if (region != null)
            {
                rects.AddRange(region.Area);
            }
        }

        writer.Write((ushort)rects.Count);
        foreach (var rect in rects)
        {
            rect.Write(writer);
        }
    }
}