using System.Buffers;
using CentrED.Client.Map;
using CentrED.Network;
using CentrED.Utility;

namespace CentrED.Client;

/// <summary>
/// Sends a login request to the server.
/// </summary>
public class LoginRequestPacket : Packet
{
    /// <summary>
    /// Initializes a login request packet.
    /// </summary>
    /// <param name="username">The login username.</param>
    /// <param name="password">The login password.</param>
    public LoginRequestPacket(string username, string password) : base(0x02, 0)
    {
        Writer.Write((byte)0x03);
        Writer.WriteStringNull(username);
        Writer.WriteStringNull(password);
    }
}

/// <summary>
/// Requests a clean disconnect.
/// </summary>
public class QuitPacket : Packet
{
    /// <summary>
    /// Initializes a quit packet.
    /// </summary>
    public QuitPacket() : base(0x02, 0)
    {
        Writer.Write((byte)0x05);
    }
}

/// <summary>
/// Requests one or more blocks from the server.
/// </summary>
public class RequestBlocksPacket : Packet
{
    /// <summary>
    /// Initializes a single-block request packet.
    /// </summary>
    /// <param name="coord">The requested block coordinate.</param>
    public RequestBlocksPacket(PointU16 coord) : base(0x04, 0)
    {
        coord.Write(Writer);
    }

    /// <summary>
    /// Initializes a multi-block request packet.
    /// </summary>
    /// <param name="coords">The requested block coordinates.</param>
    public RequestBlocksPacket(IEnumerable<PointU16> coords) : base(0x04, 0)
    {
        foreach (var blockCoord in coords)
        {
            blockCoord.Write(Writer);
        }
    }
}

/// <summary>
/// Releases a block subscription on the server.
/// </summary>
public class FreeBlockPacket : Packet
{
    /// <summary>
    /// Initializes a free-block packet.
    /// </summary>
    /// <param name="x">The block X coordinate.</param>
    /// <param name="y">The block Y coordinate.</param>
    public FreeBlockPacket(ushort x, ushort y) : base(0x05, 5)
    {
        Writer.Write(x);
        Writer.Write(y);
    }
}

/// <summary>
/// Requests a land-tile replacement or elevation.
/// </summary>
public class DrawMapPacket : Packet
{
    /// <summary>
    /// Gets the tile X coordinate.
    /// </summary>
    public ushort X { get; }

    /// <summary>
    /// Gets the tile Y coordinate.
    /// </summary>
    public ushort Y { get; }

    /// <summary>
    /// Gets the destination altitude.
    /// </summary>
    public sbyte Z { get; }

    /// <summary>
    /// Gets the destination land tile id.
    /// </summary>
    public ushort TileId { get; }
    
    /// <summary>
    /// Initializes a packet from an existing land tile.
    /// </summary>
    /// <param name="tile">The source land tile.</param>
    public DrawMapPacket(LandTile tile) : this(tile.X, tile.Y, tile.Z, tile.RealId)
    {
    }

    /// <summary>
    /// Initializes a packet with an explicit tile id and altitude.
    /// </summary>
    /// <param name="tile">The source land tile.</param>
    /// <param name="newId">The destination land tile id.</param>
    /// <param name="newZ">The destination altitude.</param>
    public DrawMapPacket(LandTile tile, ushort newId, sbyte newZ) : this(tile.X, tile.Y, newZ, newId)
    {
    }

    /// <summary>
    /// Initializes a packet with an explicit altitude.
    /// </summary>
    /// <param name="tile">The source land tile.</param>
    /// <param name="newZ">The destination altitude.</param>
    public DrawMapPacket(LandTile tile, sbyte newZ) : this(tile.X, tile.Y, newZ, tile.RealId)
    {
    }

    /// <summary>
    /// Initializes a draw-map packet.
    /// </summary>
    /// <param name="x">The tile X coordinate.</param>
    /// <param name="y">The tile Y coordinate.</param>
    /// <param name="z">The destination altitude.</param>
    /// <param name="tileId">The destination land tile id.</param>
    public DrawMapPacket(ushort x, ushort y, sbyte z, ushort tileId) : base(0x06, 8)
    {
        X = x;
        Y = y;
        Z = z;
        TileId = tileId;
        Writer.Write(x);
        Writer.Write(y);
        Writer.Write(z);
        Writer.Write(tileId);
    }
}

/// <summary>
/// Requests a static insertion.
/// </summary>
public class InsertStaticPacket : Packet
{
    /// <summary>
    /// Gets the tile X coordinate.
    /// </summary>
    public ushort X { get; }
    /// <summary>
    /// Gets the tile Y coordinate.
    /// </summary>
    public ushort Y { get; }
    /// <summary>
    /// Gets the static altitude.
    /// </summary>
    public sbyte Z { get; }
    /// <summary>
    /// Gets the static tile id.
    /// </summary>
    public ushort TileId { get; }
    /// <summary>
    /// Gets the static hue.
    /// </summary>
    public ushort Hue { get; }
    
    /// <summary>
    /// Initializes a packet from an existing static tile.
    /// </summary>
    /// <param name="tile">The static tile.</param>
    public InsertStaticPacket(StaticTile tile) : this(tile.X, tile.Y, tile.Z, tile.Id, tile.Hue)
    {
    }

    /// <summary>
    /// Initializes an insert-static packet.
    /// </summary>
    /// <param name="x">The tile X coordinate.</param>
    /// <param name="y">The tile Y coordinate.</param>
    /// <param name="z">The static altitude.</param>
    /// <param name="tileId">The static tile id.</param>
    /// <param name="hue">The static hue.</param>
    public InsertStaticPacket(ushort x, ushort y, sbyte z, ushort tileId, ushort hue) : base(0x07, 10)
    {
        X = x;
        Y = y;
        Z = z;
        TileId = tileId;
        Hue = hue;
        Writer.Write(x);
        Writer.Write(y);
        Writer.Write(z);
        Writer.Write(tileId);
        Writer.Write(hue);
    }
}

/// <summary>
/// Requests a static deletion.
/// </summary>
public class DeleteStaticPacket : Packet
{
    /// <summary>
    /// Gets the tile X coordinate.
    /// </summary>
    public ushort X { get; }
    /// <summary>
    /// Gets the tile Y coordinate.
    /// </summary>
    public ushort Y { get; }
    /// <summary>
    /// Gets the static altitude.
    /// </summary>
    public sbyte Z { get; }
    /// <summary>
    /// Gets the static tile id.
    /// </summary>
    public ushort TileId { get; }
    /// <summary>
    /// Gets the static hue.
    /// </summary>
    public ushort Hue { get; }
    
    /// <summary>
    /// Initializes a delete-static packet.
    /// </summary>
    /// <param name="x">The tile X coordinate.</param>
    /// <param name="y">The tile Y coordinate.</param>
    /// <param name="z">The static altitude.</param>
    /// <param name="tileId">The static tile id.</param>
    /// <param name="hue">The static hue.</param>
    public DeleteStaticPacket(ushort x, ushort y, sbyte z, ushort tileId, ushort hue) : base(0x08, 10)
    {
        X = x;
        Y = y;
        Z = z;
        TileId = tileId;
        Hue = hue;
        Writer.Write(x);
        Writer.Write(y);
        Writer.Write(z);
        Writer.Write(tileId);
        Writer.Write(hue);
    }

    /// <summary>
    /// Initializes a packet from an existing static tile.
    /// </summary>
    /// <param name="tile">The static tile.</param>
    public DeleteStaticPacket(StaticTile tile) : this(tile.X, tile.Y, tile.Z, tile.Id, tile.Hue)
    {
    }
}

/// <summary>
/// Requests a static elevation change.
/// </summary>
public class ElevateStaticPacket : Packet
{
    public ushort X { get; }
    public ushort Y { get; }
    public sbyte Z { get; }
    public ushort TileId { get; }
    public ushort Hue { get; }
    public sbyte NewZ { get; }
    
    /// <summary>
    /// Initializes an elevate-static packet.
    /// </summary>
    public ElevateStaticPacket(ushort x, ushort y, sbyte z, ushort tileId, ushort hue, sbyte newZ) : base(0x09, 11)
    {
        X = x;
        Y = y;
        Z = z;
        TileId = tileId;
        NewZ = newZ;
        Hue = hue;
        Writer.Write(x);
        Writer.Write(y);
        Writer.Write(z);
        Writer.Write(tileId);
        Writer.Write(hue);
        Writer.Write(newZ);
    }

    /// <summary>
    /// Initializes a packet from an existing static tile.
    /// </summary>
    /// <param name="tile">The static tile.</param>
    /// <param name="newZ">The destination altitude.</param>
    public ElevateStaticPacket(StaticTile tile, sbyte newZ) : this(tile.X, tile.Y, tile.Z, tile.Id, tile.Hue, newZ)
    {
    }
}

/// <summary>
/// Requests a static move.
/// </summary>
public class MoveStaticPacket : Packet
{
    public ushort X { get; }
    public ushort Y { get; }
    public sbyte Z { get; }
    public ushort TileId { get; }
    public ushort Hue { get; }
    public ushort NewX { get; }
    public ushort NewY { get; }
    
    /// <summary>
    /// Initializes a move-static packet.
    /// </summary>
    public MoveStaticPacket(ushort x, ushort y, sbyte z, ushort tileId, ushort hue, ushort newX, ushort newY) : base
        (0x0A, 14)
    {
        X = x;
        Y = y;
        Z = z;
        TileId = tileId;
        Hue = hue;
        NewX = newX;
        NewY = newY;
        Writer.Write(x);
        Writer.Write(y);
        Writer.Write(z);
        Writer.Write(tileId);
        Writer.Write(hue);
        Writer.Write(newX);
        Writer.Write(newY);
    }

    /// <summary>
    /// Initializes a packet from an existing static tile.
    /// </summary>
    /// <param name="tile">The static tile.</param>
    /// <param name="newX">The destination X coordinate.</param>
    /// <param name="newY">The destination Y coordinate.</param>
    public MoveStaticPacket(StaticTile tile, ushort newX, ushort newY) : this
        (tile.X, tile.Y, tile.Z, tile.Id, tile.Hue, newX, newY)
    {
    }
}

/// <summary>
/// Requests a static hue change.
/// </summary>
public class HueStaticPacket : Packet
{
    public ushort X { get; }
    public ushort Y { get; }
    public sbyte Z { get; }
    public ushort TileId { get; }
    public ushort Hue { get; }
    public ushort NewHue { get; }
    
    /// <summary>
    /// Initializes a hue-static packet.
    /// </summary>
    public HueStaticPacket(ushort x, ushort y, sbyte z, ushort tileId, ushort hue, ushort newHue) : base(0x0B, 12)
    {
        X = x;
        Y = y;
        Z = z;
        TileId = tileId;
        Hue = hue;
        NewHue = newHue;
        Writer.Write(x);
        Writer.Write(y);
        Writer.Write(z);
        Writer.Write(tileId);
        Writer.Write(hue);
        Writer.Write(newHue);
    }

    /// <summary>
    /// Initializes a packet from an existing static tile.
    /// </summary>
    /// <param name="tile">The static tile.</param>
    /// <param name="newHue">The destination hue.</param>
    public HueStaticPacket(StaticTile tile, ushort newHue) : this(tile.X, tile.Y, tile.Z, tile.Id, tile.Hue, newHue)
    {
    }
}

/// <summary>
/// Updates the client position on the server.
/// </summary>
public class UpdateClientPosPacket : Packet
{
    /// <summary>
    /// Initializes an update-client-position packet.
    /// </summary>
    /// <param name="x">The tile X coordinate.</param>
    /// <param name="y">The tile Y coordinate.</param>
    public UpdateClientPosPacket(ushort x, ushort y) : base(0x0C, 0)
    {
        Writer.Write((byte)0x04);
        Writer.Write(x);
        Writer.Write(y);
    }
}

/// <summary>
/// Sends a chat message.
/// </summary>
public class ChatMessagePacket : Packet
{
    /// <summary>
    /// Initializes a chat-message packet.
    /// </summary>
    /// <param name="message">The message text.</param>
    public ChatMessagePacket(string message) : base(0x0C, 0)
    {
        Writer.Write((byte)0x05);
        Writer.WriteStringNull(message);
    }
}

/// <summary>
/// Requests a jump to another client's position.
/// </summary>
public class GotoClientPosPacket : Packet
{
    /// <summary>
    /// Initializes a goto-client-position packet.
    /// </summary>
    /// <param name="username">The target username.</param>
    public GotoClientPosPacket(string username) : base(0x0C, 0)
    {
        Writer.Write((byte)0x06);
        Writer.WriteStringNull(username);
    }
}

/// <summary>
/// Requests a password change.
/// </summary>
public class ChangePasswordPacket : Packet
{
    /// <summary>
    /// Initializes a change-password packet.
    /// </summary>
    /// <param name="oldPassword">The old password.</param>
    /// <param name="newPassword">The new password.</param>
    public ChangePasswordPacket(string oldPassword, string newPassword) : base(0x0C, 0)
    {
        Writer.Write((byte)0x08);
        Writer.WriteStringNull(oldPassword);
        Writer.WriteStringNull(newPassword);
    }
}

/// <summary>
/// Requests the current radar checksum.
/// </summary>
public class RequestRadarChecksumPacket : Packet
{
    /// <summary>
    /// Initializes a radar-checksum request packet.
    /// </summary>
    public RequestRadarChecksumPacket() : base(0x0D, 2)
    {
        Writer.Write((byte)0x01);
    }
}

/// <summary>
/// Requests the full radar map.
/// </summary>
public class RequestRadarMapPacket : Packet
{
    /// <summary>
    /// Initializes a radar-map request packet.
    /// </summary>
    public RequestRadarMapPacket() : base(0x0D, 2)
    {
        Writer.Write((byte)0x02);
    }
}

/// <summary>
/// Sends a compound large-scale operation definition.
/// </summary>
public class LargeScaleOperationPacket : Packet
{
    /// <summary>
    /// Initializes a large-scale operation packet.
    /// </summary>
    /// <param name="areas">The target rectangles.</param>
    /// <param name="lso">The large-scale operation to serialize.</param>
    public LargeScaleOperationPacket(RectU16[] areas, ILargeScaleOperation lso) : base(0x0E, 0)
    {
        Writer.Write((byte)Math.Min(255, areas.Length));
        foreach (var areaInfo in areas)
        {
            areaInfo.Write(Writer);
        }

        WriteLso(lso, typeof(LSOCopyMove));
        WriteLso(lso, typeof(LSOSetAltitude));
        WriteLso(lso, typeof(LSODrawLand));
        WriteLso(lso, typeof(LSODeleteStatics));
        WriteLso(lso, typeof(LSOAddStatics));
    }

    /// <summary>
    /// Writes one optional large-scale operation payload.
    /// </summary>
    /// <param name="lso">The operation instance.</param>
    /// <param name="type">The expected operation type.</param>
    private void WriteLso(ILargeScaleOperation lso, Type type)
    {
        if (lso.GetType() == type)
        {
            Writer.Write(true);
            lso.Write(Writer);
        }
        else
        {
            Writer.Write(false);
        }
    }
}

/// <summary>
/// Sends a keep-alive packet.
/// </summary>
public class NoOpPacket : Packet
{
    /// <summary>
    /// Initializes a no-op packet.
    /// </summary>
    public NoOpPacket() : base(0xFF, 1)
    {
    }
}

/// <summary>
/// Provides a base packet for administrative requests.
/// </summary>
public abstract class AdminPacket : Packet
{
    /// <summary>
    /// Initializes an administrative packet with the supplied subcommand.
    /// </summary>
    /// <param name="packetId">The administrative subcommand id.</param>
    public AdminPacket(byte packetId) : base(0x03, 0)
    {
        Writer.Write(packetId);
    }
}

/// <summary>
/// Requests an immediate server flush.
/// </summary>
public class ServerFlushPacket : AdminPacket
{
    /// <summary>
    /// Initializes a server-flush packet.
    /// </summary>
    public ServerFlushPacket() : base(0x01)
    {
    }
}

/// <summary>
/// Requests a server stop.
/// </summary>
public class ServerStopPacket : AdminPacket
{
    /// <summary>
    /// Initializes a server-stop packet.
    /// </summary>
    /// <param name="reason">The stop reason.</param>
    public ServerStopPacket(string reason) : base(0x02)
    {
        Writer.WriteStringNull(reason);
    }
}

/// <summary>
/// Adds or modifies a user definition.
/// </summary>
public class ModifyUserPacket : AdminPacket
{
    /// <summary>
    /// Initializes a modify-user packet.
    /// </summary>
    public ModifyUserPacket(string username, string password, AccessLevel accessLevel, List<string> regions) : base(0x05)
    {
        Writer.WriteStringNull(username);
        Writer.WriteStringNull(password);
        Writer.Write((byte)accessLevel);
        Writer.Write((byte)regions.Count);
        foreach (var region in regions)
        {
            Writer.WriteStringNull(region);
        }
    }
}

/// <summary>
/// Deletes a user definition.
/// </summary>
public class DeleteUserPacket : AdminPacket
{
    /// <summary>
    /// Initializes a delete-user packet.
    /// </summary>
    /// <param name="username">The username to delete.</param>
    public DeleteUserPacket(string username) : base(0x06)
    {
        Writer.WriteStringNull(username);
    }
}

/// <summary>
/// Requests the current user list.
/// </summary>
public class ListUsersPacket : AdminPacket
{
    /// <summary>
    /// Initializes a list-users packet.
    /// </summary>
    public ListUsersPacket() : base(0x07)
    {
    }
}

/// <summary>
/// Adds or modifies a region definition.
/// </summary>
public class ModifyRegionPacket : AdminPacket
{
    /// <summary>
    /// Initializes a modify-region packet.
    /// </summary>
    public ModifyRegionPacket(string regionName, List<RectU16> areas) : base(0x08)
    {
        Writer.WriteStringNull(regionName);
        Writer.Write((byte)areas.Count);
        foreach (var area in areas)
        {
            area.Write(Writer);
        }
    }
}

/// <summary>
/// Deletes a region definition.
/// </summary>
public class DeleteRegionPacket : AdminPacket
{
    /// <summary>
    /// Initializes a delete-region packet.
    /// </summary>
    /// <param name="regionName">The region name to delete.</param>
    public DeleteRegionPacket(string regionName) : base(0x09)
    {
        Writer.WriteStringNull(regionName);
    }
}

/// <summary>
/// Requests the current region list.
/// </summary>
public class ListRegionsPacket : AdminPacket
{
    /// <summary>
    /// Initializes a list-regions packet.
    /// </summary>
    public ListRegionsPacket() : base(0x0A)
    {
    }
}

/// <summary>
/// Provides helpers for ad-hoc administrative packet payloads.
/// </summary>
public static class AdminPackets
{
    /// <summary>
    /// Sends the administrative CPU-idle toggle packet.
    /// </summary>
    /// <param name="client">The target client.</param>
    /// <param name="enabled"><see langword="true"/> to enable idle sleeping; otherwise, <see langword="false"/>.</param>
    public static void SendAdminSetIdleCpu(this CentrEDClient client, bool enabled)
    {
        var writer = new SpanWriter(stackalloc byte[7]);
        writer.Write((byte)0x03);
        writer.Write((uint)7);
        writer.Write((byte)0x10);
        writer.Write(enabled);
        client.Send(writer.Span);
    }
}