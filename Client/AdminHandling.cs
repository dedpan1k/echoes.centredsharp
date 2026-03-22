using System.Buffers;
using CentrED.Network;

namespace CentrED.Client;

/// <summary>
/// Represents a callback raised when a user is deleted through the administrative API.
/// </summary>
/// <param name="username">The deleted username.</param>
public static class AdminHandling
{
    /// <summary>
    /// Represents a callback raised when a user is modified through the administrative API.
    /// </summary>
    /// <param name="username">The affected username.</param>
    /// <param name="status">The modification status.</param>
    public delegate void UserDeleted(string username);

    /// <summary>
    /// Represents a callback raised when a region is deleted through the administrative API.
    /// </summary>
    /// <param name="username">The affected username.</param>
    public delegate void UserModified(string username, ModifyUserStatus status);

    /// <summary>
    /// Represents a callback raised when a region is deleted through the administrative API.
    /// </summary>
    /// <param name="name">The deleted region name.</param>
    public delegate void RegionDeleted(string name);

    /// <summary>
    /// Represents a callback raised when a region is modified through the administrative API.
    /// </summary>
    /// <param name="name">The affected region name.</param>
    /// <param name="status">The modification status.</param>
    public delegate void RegionModified(string name, ModifyRegionStatus status);
    
    // Administrative packets are routed by one-byte subcommand just like the server side.
    private static PacketHandler<CentrEDClient>?[] Handlers { get; }

    static AdminHandling()
    {
        Handlers = new PacketHandler<CentrEDClient>?[0x100];

        Handlers[0x05] = new PacketHandler<CentrEDClient>(0, OnModifyUserResponsePacket);
        Handlers[0x06] = new PacketHandler<CentrEDClient>(0, OnDeleteUserResponsePacket);
        Handlers[0x07] = new PacketHandler<CentrEDClient>(0, OnListUsersResponsePacket);
        Handlers[0x08] = new PacketHandler<CentrEDClient>(0, OnModifyRegionResponsePacket);
        Handlers[0x09] = new PacketHandler<CentrEDClient>(0, OnDeleteRegionResponsePacket);
        Handlers[0x0A] = new PacketHandler<CentrEDClient>(0, OnListRegionsResponsePacket);
    }

    /// <summary>
    /// Dispatches an incoming administrative packet.
    /// </summary>
    /// <param name="reader">The packet reader positioned after the outer header.</param>
    /// <param name="ns">The client network session.</param>
    public static void OnAdminHandlerPacket(SpanReader reader, NetState<CentrEDClient> ns)
    {
        ns.LogDebug("Client OnConnectionHandlerPacket");
        var id = reader.ReadByte();
        var packetHandler = Handlers[id];
        packetHandler?.OnReceive(reader, ns);
    }
    
    /// <summary>
    /// Updates the cached user list after an add or modify response.
    /// </summary>
    /// <param name="reader">The packet payload reader.</param>
    /// <param name="ns">The client network session.</param>
    private static void OnModifyUserResponsePacket(SpanReader reader, NetState<CentrEDClient> ns)
    {
        ns.LogDebug("Client OnModifyUserResponsePacket");
        var status = (ModifyUserStatus)reader.ReadByte();
        if (status == ModifyUserStatus.InvalidUsername)
            return;
        
        var username = reader.ReadString();
        var accessLevel = (AccessLevel)reader.ReadByte();
        var regionCount = reader.ReadByte();
        var regions = new List<string>(regionCount);
        for (var i = 0; i < regionCount; i++)
        {
            var regionName = reader.ReadString();
            regions.Add(regionName);
        }
        var user = new User(username, accessLevel, regions);
        if (status == ModifyUserStatus.Added)
        {
            ns.Parent.Admin.Users.Add(user);
        }
        if(status == ModifyUserStatus.Modified)
        {
            var index = ns.Parent.Admin.Users.FindIndex(u => u.Username == username);
            ns.Parent.Admin.Users[index] = user;
        }
        ns.Parent.OnUserModified(username, status);
    }
    
    /// <summary>
    /// Removes a cached user after a delete response.
    /// </summary>
    /// <param name="reader">The packet payload reader.</param>
    /// <param name="ns">The client network session.</param>
    private static void OnDeleteUserResponsePacket(SpanReader reader, NetState<CentrEDClient> ns)
    {
        ns.LogDebug("Client OnDeleteUserResponsePacket");
        var status = (DeleteUserStatus)reader.ReadByte();
        var username = reader.ReadString();
        if (status != DeleteUserStatus.Deleted)
            return;

        ns.Parent.Admin.Users.RemoveAll(u => u.Username == username);
        ns.Parent.OnUserDeleted(username);
    }
    
    /// <summary>
    /// Replaces the cached user list with the server snapshot.
    /// </summary>
    /// <param name="reader">The packet payload reader.</param>
    /// <param name="ns">The client network session.</param>
    private static void OnListUsersResponsePacket(SpanReader reader, NetState<CentrEDClient> ns)
    {
        ns.LogDebug("Client OnListUsersResponsePacket");
        ns.Parent.Admin.Users.Clear();
        var userCount = reader.ReadUInt16();
        ns.Parent.Admin.Users.Capacity = userCount;
        for (var i = 0; i < userCount; i++)
        {
            var username = reader.ReadString();
            var accessLevel = (AccessLevel)reader.ReadByte();
            var regionCount = reader.ReadByte();
            var regions = new List<string>(regionCount);
            for (var j = 0; j < regionCount; j++)
            {
                regions.Add(reader.ReadString());
            }
            ns.Parent.Admin.Users.Add(new User(username, accessLevel, regions));
        }
    }
    
    /// <summary>
    /// Updates the cached region list after an add or modify response.
    /// </summary>
    /// <param name="reader">The packet payload reader.</param>
    /// <param name="ns">The client network session.</param>
    private static void OnModifyRegionResponsePacket(SpanReader reader, NetState<CentrEDClient> ns)
    {
        ns.LogDebug("Client OnModifyRegionResponsePacket");
        var status = (ModifyRegionStatus)reader.ReadByte();
        var regionName = reader.ReadString();
        var areaCount = reader.ReadByte();
        var areas = new List<RectU16>(areaCount);
        for (var i = 0; i < areaCount; i++)
        {
            var newArea = reader.ReadRectU16();
            areas.Add(newArea);
        }
        var region = new Region(regionName, areas);
        if(status == ModifyRegionStatus.Added)
        {
            ns.Parent.Admin.Regions.Add(region);
        }
        if(status == ModifyRegionStatus.Modified)
        {
            var index = ns.Parent.Admin.Regions.FindIndex(r => r.Name == regionName);
            ns.Parent.Admin.Regions[index] = region;
        }
        ns.Parent.OnRegionModified(regionName, status);
    }
    
    /// <summary>
    /// Removes a cached region after a delete response.
    /// </summary>
    /// <param name="reader">The packet payload reader.</param>
    /// <param name="ns">The client network session.</param>
    private static void OnDeleteRegionResponsePacket(SpanReader reader, NetState<CentrEDClient> ns)
    {
        ns.LogDebug("Client OnDeleteRegionResponsePacket");
        var status = (DeleteRegionStatus)reader.ReadByte();
        var regionName = reader.ReadString();
        if (status == DeleteRegionStatus.NotFound)
            return;
        
        var region = ns.Parent.Admin.Regions.Find(r => r.Name == regionName);
        ns.Parent.Admin.Regions.Remove(region);
        foreach (var users in ns.Parent.Admin.Users)
        {
            users.Regions.Remove(regionName);
        }
        ns.Parent.OnRegionDeleted(regionName);
    }
    
    /// <summary>
    /// Replaces the cached region list with the server snapshot.
    /// </summary>
    /// <param name="reader">The packet payload reader.</param>
    /// <param name="ns">The client network session.</param>
    private static void OnListRegionsResponsePacket(SpanReader reader, NetState<CentrEDClient> ns)
    {
        ns.LogDebug("Client OnListRegionsResponsePacket");
        var regionCount = reader.ReadByte();
        ns.Parent.Admin.Regions = new List<Region>(regionCount);
        for (var i = 0; i < regionCount; i++)
        {
            var regionName = reader.ReadString();
            var areaCount = reader.ReadByte();
            var areas = new List<RectU16>(areaCount);
            for (var j = 0; j < areaCount; j++)
            {
                areas.Add(reader.ReadRectU16());
            }
            var region = new Region(regionName, areas);
            ns.Parent.Admin.Regions.Add(region);
        }
    }
}