using System.Buffers;
using CentrED.Network;
using CentrED.Server.Config;

namespace CentrED.Server;

/// <summary>
/// Handles administrative packets that manage users, regions, persistence, and server runtime flags.
/// </summary>
public class AdminHandling
{
    // Admin packets also use one-byte subcommand identifiers, but access checks are
    // layered on top because some operations are developer-only while others require administrator rights.
    private static PacketHandler<CEDServer>?[] Handlers { get; }

    static AdminHandling()
    {
        Handlers = new PacketHandler<CEDServer>?[0x100];

        Handlers[0x01] = new PacketHandler<CEDServer>(0, OnFlushPacket);
        Handlers[0x02] = new PacketHandler<CEDServer>(0, OnShutdownPacket);
        Handlers[0x05] = new PacketHandler<CEDServer>(0, OnModifyUserPacket);
        Handlers[0x06] = new PacketHandler<CEDServer>(0, OnDeleteUserPacket);
        Handlers[0x07] = new PacketHandler<CEDServer>(0, OnListUsersPacket);
        Handlers[0x08] = new PacketHandler<CEDServer>(0, OnModifyRegionPacket);
        Handlers[0x09] = new PacketHandler<CEDServer>(0, OnDeleteRegionPacket);
        Handlers[0x0A] = new PacketHandler<CEDServer>(0, OnListRegionsPacket);
        Handlers[0x10] = new PacketHandler<CEDServer>(0, OnServerCpuIdlePacket);
    }

    /// <summary>
    /// Dispatches an administrative packet after validating the sender's access level.
    /// </summary>
    /// <param name="reader">The packet payload reader positioned after the outer packet header.</param>
    /// <param name="ns">The client session that sent the packet.</param>
    public static void OnAdminHandlerPacket(SpanReader reader, NetState<CEDServer> ns)
    {
        ns.LogDebug("Server OnAdminHandlerPacket");
        if (!ns.ValidateAccess(AccessLevel.Developer))
            return;
        var id = reader.ReadByte();
        if (id != 0x01 && id != 0x10 && !ns.ValidateAccess(AccessLevel.Administrator))
            return;
        var packetHandler = Handlers[id];
        packetHandler?.OnReceive(reader, ns);
    }

    /// <summary>
    /// Flushes the landscape and configuration to disk immediately.
    /// </summary>
    /// <param name="reader">The payload reader.</param>
    /// <param name="ns">The client session requesting the flush.</param>
    private static void OnFlushPacket(SpanReader reader, NetState<CEDServer> ns)
    {
        ns.LogDebug("Server OnFlushPacket");
        ns.Parent.Landscape.Flush();
        ns.Parent.Config.Flush();
    }

    /// <summary>
    /// Requests a graceful server shutdown.
    /// </summary>
    /// <param name="reader">The payload reader.</param>
    /// <param name="ns">The client session requesting shutdown.</param>
    private static void OnShutdownPacket(SpanReader reader, NetState<CEDServer> ns)
    {
        ns.LogDebug("Server OnShutdownPacket");
        ns.Parent.Quit = true;
    }

    /// <summary>
    /// Adds a new account or updates an existing account definition.
    /// </summary>
    /// <param name="reader">The payload reader containing account data and region assignments.</param>
    /// <param name="ns">The client session performing the modification.</param>
    private static void OnModifyUserPacket(SpanReader reader, NetState<CEDServer> ns)
    {
        ns.LogDebug("Server OnModifyUserPacket");
        var username = reader.ReadString();
        var password = reader.ReadString();
        var accessLevel = (AccessLevel)reader.ReadByte();
        var regionCount = reader.ReadByte();

        var account = ns.Parent.GetAccount(username);
        if (account != null)
        {
            if (password != "")
            {
                account.UpdatePassword(password);
            }

            account.AccessLevel = accessLevel;
            account.Regions.Clear();
            for (int i = 0; i < regionCount; i++)
            {
                account.Regions.Add(reader.ReadString());
            }

            ns.Parent.Config.Invalidate();

            // Existing clients need to refresh their access state if their account changed.
            ns.Parent.GetClient(account.Name)?.Send(new AccessChangedPacket(ns));
            ns.Send(new ModifyUserResponsePacket(ModifyUserStatus.Modified, account));
        }
        else
        {
            if (username == "")
            {
                ns.Send(new ModifyUserResponsePacket(ModifyUserStatus.InvalidUsername, account));
            }
            else
            {
                var regions = new List<string>();
                for (int i = 0; i < regionCount; i++)
                {
                    regions.Add(reader.ReadString());
                }

                account = new Account(username, password, accessLevel, regions);
                ns.Parent.Config.Accounts.Add(account);
                ns.Parent.Config.Invalidate();
                ns.Send(new ModifyUserResponsePacket(ModifyUserStatus.Added, account));
            }
        }
    }

    /// <summary>
    /// Deletes an account unless it belongs to the requesting administrator.
    /// </summary>
    /// <param name="reader">The payload reader containing the username to remove.</param>
    /// <param name="ns">The client session performing the deletion.</param>
    private static void OnDeleteUserPacket(SpanReader reader, NetState<CEDServer> ns)
    {
        ns.LogDebug("Server OnDeleteUserPacket");
        var username = reader.ReadString();
        var account = ns.Parent.GetAccount(username);
        if (account != null && account.Name != ns.Username)
        {
            ns.Parent.GetClient(account.Name)?.Disconnect();
            ns.Parent.Config.Accounts.Remove(account);
            ns.Parent.Config.Invalidate();
            ns.Send(new DeleteUserResponsePacket(DeleteUserStatus.Deleted, username));
        }
        else
        {
            ns.Send(new DeleteUserResponsePacket(DeleteUserStatus.NotFound, username));
        }
    }

    /// <summary>
    /// Sends the full configured user list back to the requesting administrator.
    /// </summary>
    /// <param name="reader">The payload reader.</param>
    /// <param name="ns">The client session requesting the list.</param>
    private static void OnListUsersPacket(SpanReader reader, NetState<CEDServer> ns)
    {
        ns.LogDebug("Server OnListUsersPacket");
        ns.SendCompressed(new UserListPacket(ns));
    }

    /// <summary>
    /// Adds or replaces a region definition and refreshes affected client permissions.
    /// </summary>
    /// <param name="reader">The payload reader containing region geometry.</param>
    /// <param name="ns">The client session performing the modification.</param>
    private static void OnModifyRegionPacket(SpanReader reader, NetState<CEDServer> ns)
    {
        ns.LogDebug("Server OnModifyRegionPacket");
        var regionName = reader.ReadString();
        if (string.IsNullOrEmpty(regionName))
        {
            ns.LogWarn("Request to edit region with empty name");
            return;
        }

        var region = ns.Parent.GetRegion(regionName);
        ModifyRegionStatus status;
        if (region == null)
        {
            region = new Region(regionName);
            ns.Parent.Config.Regions.Add(region);
            status = ModifyRegionStatus.Added;
        }
        else
        {
            region.Area.Clear();
            status = ModifyRegionStatus.Modified;
        }

        var areaCount = reader.ReadByte();
        for (int i = 0; i < areaCount; i++)
        {
            region.Area.Add(reader.ReadRectU16());
        }

        ns.Parent.Config.Invalidate();
        AdminBroadcast(ns, AccessLevel.Administrator, new ModifyRegionResponsePacket(status, region));

        if (status == ModifyRegionStatus.Modified)
        {
            // Region edits can immediately change who may edit an area, so any client
            // assigned to the region receives refreshed restrictions.
            foreach (var netState in ns.Parent.Clients)
            {
                var account = ns.Parent.GetAccount(netState.Username)!;

                if (account.Regions.Contains(regionName))
                {
                    netState.Send(new AccessChangedPacket(ns));
                }
            }
        }
    }

    /// <summary>
    /// Deletes a region and removes it from every account assignment.
    /// </summary>
    /// <param name="reader">The payload reader containing the region name.</param>
    /// <param name="ns">The client session performing the deletion.</param>
    private static void OnDeleteRegionPacket(SpanReader reader, NetState<CEDServer> ns)
    {
        ns.LogDebug("Server OnDeleteRegionPacket");
        var regionName = reader.ReadString();
        var status = DeleteRegionStatus.NotFound;
        var region = ns.Parent.GetRegion(regionName);
        if (region != null)
        {
            ns.Parent.Config.Regions.Remove(region);
            foreach (var account in ns.Parent.Config.Accounts)
            {
                account.Regions.Remove(regionName);
            }
            ns.Parent.Config.Invalidate();
            status = DeleteRegionStatus.Deleted;
        }

        AdminBroadcast(ns, AccessLevel.Administrator, new DeleteRegionResponsePacket(status, regionName));
    }

    /// <summary>
    /// Enables or disables main-loop idle sleeping for diagnostics or performance testing.
    /// </summary>
    /// <param name="reader">The payload reader containing the requested idle flag.</param>
    /// <param name="ns">The client session requesting the change.</param>
    private static void OnServerCpuIdlePacket(SpanReader reader, NetState<CEDServer> ns)
    {
        ns.LogDebug("Server OnServerTurboPacket");
        var enabled = reader.ReadBoolean();
        ns.Parent.SetCPUIdle(ns, enabled);
    }

    /// <summary>
    /// Sends the full configured region list back to the requesting administrator.
    /// </summary>
    /// <param name="reader">The payload reader.</param>
    /// <param name="ns">The client session requesting the list.</param>
    private static void OnListRegionsPacket(SpanReader reader, NetState<CEDServer> ns)
    {
        ns.LogDebug("Server OnListRegionsPacket");
        ns.SendCompressed(new RegionListPacket(ns));
    }

    /// <summary>
    /// Broadcasts an administrative packet to connected clients that meet the requested access level.
    /// </summary>
    /// <param name="ns">The initiating client session.</param>
    /// <param name="accessLevel">The minimum access level required to receive the packet.</param>
    /// <param name="packet">The packet to send.</param>
    private static void AdminBroadcast(NetState<CEDServer> ns, AccessLevel accessLevel, Packet packet)
    {
        ns.LogDebug("AdminBroadcast");
        foreach (var netState in ns.Parent.Clients)
        {
            if (ns.Parent.GetAccount(netState.Username)!.AccessLevel >= accessLevel)
            {
                netState.Send(packet);
            }
        }
    }
}