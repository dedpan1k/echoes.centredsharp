using CentrED.Network;
using CentrED.Server.Config;
using CentrED.Utility;

namespace CentrED.Server;

/// <summary>
/// Carries the serialized contents of one or more subscribed map blocks.
/// </summary>
class BlockPacket : Packet
{
    /// <summary>
    /// Initializes a packet containing the requested map blocks and their statics.
    /// </summary>
    /// <param name="coords">The block coordinates to serialize.</param>
    /// <param name="ns">The client session requesting the data.</param>
    public BlockPacket(IEnumerable<PointU16> coords, NetState<CEDServer> ns) : base(0x04, 0)
    {
        foreach (var coord in coords)
        {
            var mapBlock = ns.Parent.Landscape.GetLandBlock(coord.X, coord.Y);
            var staticsBlock = ns.Parent.Landscape.GetStaticBlock(coord.X, coord.Y);

            coord.Write(Writer);
            mapBlock.Write(Writer);
            // Static blocks must be sorted against tiledata before serialization so
            // the client receives the same draw order it expects for live updates.
            Writer.Write((ushort)staticsBlock.TotalTilesCount);
            staticsBlock.SortTiles(ref ns.Parent.Landscape.TileDataProvider.StaticTiles);
            staticsBlock.Write(Writer);
        }
    }
}

/// <summary>
/// Broadcasts an updated land tile after a draw-map operation.
/// </summary>
class DrawMapPacket : Packet
{
    /// <summary>
    /// Initializes a packet for a single updated land tile.
    /// </summary>
    /// <param name="landTile">The land tile to serialize.</param>
    public DrawMapPacket(LandTile landTile) : base(0x06, 8)
    {
        Writer.Write(landTile.X);
        Writer.Write(landTile.Y);
        Writer.Write(landTile.Z);
        Writer.Write(landTile.Id);
    }
}

/// <summary>
/// Broadcasts a newly inserted static tile.
/// </summary>
class InsertStaticPacket : Packet
{
    /// <summary>
    /// Initializes a packet for a single inserted static tile.
    /// </summary>
    /// <param name="staticTile">The static tile to serialize.</param>
    public InsertStaticPacket(StaticTile staticTile) : base(0x07, 10)
    {
        Writer.Write(staticTile.X);
        Writer.Write(staticTile.Y);
        Writer.Write(staticTile.Z);
        Writer.Write(staticTile.Id);
        Writer.Write(staticTile.Hue);
    }
}

/// <summary>
/// Broadcasts the removal of an existing static tile.
/// </summary>
class DeleteStaticPacket : Packet
{
    /// <summary>
    /// Initializes a packet for a single removed static tile.
    /// </summary>
    /// <param name="staticTile">The static tile that was removed.</param>
    public DeleteStaticPacket(StaticTile staticTile) : base(0x08, 10)
    {
        Writer.Write(staticTile.X);
        Writer.Write(staticTile.Y);
        Writer.Write(staticTile.Z);
        Writer.Write(staticTile.Id);
        Writer.Write(staticTile.Hue);
    }
}

/// <summary>
/// Broadcasts a Z-axis change for a static tile.
/// </summary>
class ElevateStaticPacket : Packet
{
    /// <summary>
    /// Initializes a packet for a static elevation change.
    /// </summary>
    /// <param name="staticTile">The static tile being elevated.</param>
    /// <param name="newZ">The new Z coordinate.</param>
    public ElevateStaticPacket(StaticTile staticTile, sbyte newZ) : base(0x09, 11)
    {
        Writer.Write(staticTile.X);
        Writer.Write(staticTile.Y);
        Writer.Write(staticTile.Z);
        Writer.Write(staticTile.Id);
        Writer.Write(staticTile.Hue);
        Writer.Write(newZ);
    }
}

/// <summary>
/// Broadcasts a static tile move between tile coordinates.
/// </summary>
class MoveStaticPacket : Packet
{
    /// <summary>
    /// Initializes a packet for a static move operation.
    /// </summary>
    /// <param name="staticTile">The static tile being moved.</param>
    /// <param name="newX">The destination X tile coordinate.</param>
    /// <param name="newY">The destination Y tile coordinate.</param>
    public MoveStaticPacket(StaticTile staticTile, ushort newX, ushort newY) : base(0x0A, 14)
    {
        Writer.Write(staticTile.X);
        Writer.Write(staticTile.Y);
        Writer.Write(staticTile.Z);
        Writer.Write(staticTile.Id);
        Writer.Write(staticTile.Hue);
        Writer.Write(newX);
        Writer.Write(newY);
    }
}

/// <summary>
/// Broadcasts a hue change for a static tile.
/// </summary>
class HueStaticPacket : Packet
{
    /// <summary>
    /// Initializes a packet for a static hue update.
    /// </summary>
    /// <param name="staticTile">The static tile being recolored.</param>
    /// <param name="newHue">The new hue value.</param>
    public HueStaticPacket(StaticTile staticTile, ushort newHue) : base(0x0B, 12)
    {
        Writer.Write(staticTile.X);
        Writer.Write(staticTile.Y);
        Writer.Write(staticTile.Z);
        Writer.Write(staticTile.Id);
        Writer.Write(staticTile.Hue);
        Writer.Write(newHue);
    }
}

/// <summary>
/// Announces the protocol flavor and version expected by the server.
/// </summary>
public class ProtocolVersionPacket : Packet
{
    /// <summary>
    /// Initializes a protocol-version packet.
    /// </summary>
    /// <param name="version">The negotiated protocol version value.</param>
    public ProtocolVersionPacket(uint version) : base(0x02, 0)
    {
        Writer.Write((byte)0x01);
        Writer.Write(version);
    }
}

/// <summary>
/// Reports the outcome of a login attempt and, on success, sends initial session metadata.
/// </summary>
public class LoginResponsePacket : Packet
{
    /// <summary>
    /// Initializes a login response packet.
    /// </summary>
    /// <param name="state">The login result.</param>
    /// <param name="ns">The authenticated session when the login succeeded.</param>
    public LoginResponsePacket(LoginState state, NetState<CEDServer>? ns = null) : base(0x02, 0)
    {
        Writer.Write((byte)0x03);
        Writer.Write((byte)state);
        if (state == LoginState.Ok && ns != null)
        {
            ns.Account().LastLogon = DateTime.Now;
            Writer.Write((byte)ns.AccessLevel());
            if (ns.ProtocolVersion == ProtocolVersion.CentrEDPlus)
                Writer.Write((uint)Math.Abs((DateTime.Now - ns.Parent.StartTime).TotalSeconds));
            Writer.Write(ns.Parent.Landscape.Width);
            Writer.Write(ns.Parent.Landscape.Height);
            if (ns.ProtocolVersion == ProtocolVersion.CentrEDPlus)
            {
                // These high bits identify the extended protocol while the lower flags
                // communicate map format capabilities expected by the client.
                uint flags = 0xF0000000;
                if (ns.Parent.Landscape.TileDataProvider.Version == TileDataVersion.HighSeas)
                    flags |= 0x8;
                if (ns.Parent.Landscape.IsUop)
                    flags |= 0x10;

                Writer.Write(flags);
            }

            ClientHandling.WriteAccountRestrictions(Writer, ns);
        }
    }
}

/// <summary>
/// Acknowledges that the server accepted a client quit request.
/// </summary>
public class QuitAckPacket : Packet
{
    /// <summary>
    /// Initializes a quit-acknowledgement packet.
    /// </summary>
    public QuitAckPacket() : base(0x02, 0)
    {
        Writer.Write((byte)0x05);
    }
}

/// <summary>
/// Broadcasts a coarse-grained server runtime state change.
/// </summary>
public class ServerStatePacket : Packet
{
    /// <summary>
    /// Initializes a server-state packet.
    /// </summary>
    /// <param name="state">The state to report.</param>
    /// <param name="message">The optional human-readable message for <see cref="ServerState.Other"/>.</param>
    public ServerStatePacket(ServerState state, string message = "") : base(0x02, 0)
    {
        Writer.Write((byte)0x04);
        Writer.Write((byte)state);
        if (state == ServerState.Other)
            Writer.WriteStringNull(message);
    }
}

/// <summary>
/// Notifies clients that a new user joined the session.
/// </summary>
public class ClientConnectedPacket : Packet
{
    /// <summary>
    /// Initializes a client-connected notification.
    /// </summary>
    /// <param name="ns">The client session that connected.</param>
    public ClientConnectedPacket(NetState<CEDServer> ns) : base(0x0C, 0)
    {
        Writer.Write((byte)0x01);
        Writer.WriteStringNull(ns.Username);
        if (ns.ProtocolVersion == ProtocolVersion.CentrEDPlus)
        {
            Writer.Write((byte)ns.AccessLevel());
        }
    }
}

/// <summary>
/// Notifies clients that a user disconnected from the session.
/// </summary>
public class ClientDisconnectedPacket : Packet
{
    /// <summary>
    /// Initializes a client-disconnected notification.
    /// </summary>
    /// <param name="ns">The client session that disconnected.</param>
    public ClientDisconnectedPacket(NetState<CEDServer> ns) : base(0x0C, 0)
    {
        Writer.Write((byte)0x02);
        Writer.WriteStringNull(ns.Username);
    }
}

/// <summary>
/// Sends the list of currently connected users to a newly authenticated client.
/// </summary>
public class ClientListPacket : Packet
{
    /// <summary>
    /// Initializes a client-list packet.
    /// </summary>
    /// <param name="avoid">The client session that should be excluded from the list.</param>
    public ClientListPacket(NetState<CEDServer> avoid) : base(0x0C, 0)
    {
        Writer.Write((byte)0x03);
        foreach (var ns in avoid.Parent.Clients)
        {
            if (ns.Username != "" && ns != avoid)
            {
                Writer.WriteStringNull(ns.Username);
                if (avoid.Parent.Config.CentrEdPlus)
                {
                    Writer.Write((byte)ns.AccessLevel());
                    Writer.Write((uint)Math.Abs((ns.LastLogon() - avoid.Parent.StartTime).TotalSeconds));
                }
            }
        }
    }
}

/// <summary>
/// Updates a client's camera position to a persisted or requested map location.
/// </summary>
public class SetClientPosPacket : Packet
{
    /// <summary>
    /// Initializes a set-position packet for the supplied session.
    /// </summary>
    /// <param name="ns">The client session whose last known position should be serialized.</param>
    public SetClientPosPacket(NetState<CEDServer> ns) : base(0x0C, 0)
    {
        Writer.Write((byte)0x04);
        Writer.Write((ushort)Math.Clamp(ns.Account().LastPos.X, 0, ns.Parent.Landscape.WidthInTiles - 1));
        Writer.Write((ushort)Math.Clamp(ns.Account().LastPos.Y, 0, ns.Parent.Landscape.HeightInTiles - 1));
    }
}

/// <summary>
/// Broadcasts a chat message to all connected users.
/// </summary>
public class ChatMessagePacket : Packet
{
    /// <summary>
    /// Initializes a chat-message packet.
    /// </summary>
    /// <param name="sender">The display name of the message sender.</param>
    /// <param name="message">The chat message text.</param>
    public ChatMessagePacket(string sender, string message) : base(0x0C, 0)
    {
        Writer.Write((byte)0x05);
        Writer.WriteStringNull(sender);
        Writer.WriteStringNull(message);
    }
}

/// <summary>
/// Refreshes the recipient's access level and editable-area restrictions.
/// </summary>
public class AccessChangedPacket : Packet
{
    /// <summary>
    /// Initializes an access-changed packet for the supplied session.
    /// </summary>
    /// <param name="ns">The client session whose access state should be serialized.</param>
    public AccessChangedPacket(NetState<CEDServer> ns) : base(0x0C, 0)
    {
        Writer.Write((byte)0x07);
        Writer.Write((byte)ns.AccessLevel());
        ClientHandling.WriteAccountRestrictions(Writer, ns);
    }
}

/// <summary>
/// Reports the outcome of a password change request.
/// </summary>
public class PasswordChangeStatusPacket : Packet
{
    /// <summary>
    /// Initializes a password-change status packet.
    /// </summary>
    /// <param name="status">The password change result.</param>
    public PasswordChangeStatusPacket(PasswordChangeStatus status) : base(0x0C, 0)
    {
        Writer.Write((byte)0x08);
        Writer.Write((byte)status);
    }
}

/// <summary>
/// Reports the outcome of a user add or modify request.
/// </summary>
public class ModifyUserResponsePacket : Packet
{
    /// <summary>
    /// Initializes a modify-user response packet.
    /// </summary>
    /// <param name="status">The result of the requested user mutation.</param>
    /// <param name="account">The resulting account state when one is available.</param>
    public ModifyUserResponsePacket(ModifyUserStatus status, Account? account) : base(0x03, 0)
    {
        Writer.Write((byte)0x05);
        Writer.Write((byte)status);

        if (account == null)
            return;

        Writer.WriteStringNull(account.Name);
        if (status == ModifyUserStatus.Added || status == ModifyUserStatus.Modified)
        {
            Writer.Write((byte)account.AccessLevel);
            Writer.Write((byte)account.Regions.Count);
            foreach (var regionName in account.Regions)
            {
                Writer.WriteStringNull(regionName);
            }
        }
    }
}

/// <summary>
/// Reports the outcome of a user deletion request.
/// </summary>
public class DeleteUserResponsePacket : Packet
{
    /// <summary>
    /// Initializes a delete-user response packet.
    /// </summary>
    /// <param name="status">The result of the requested deletion.</param>
    /// <param name="username">The username that was targeted.</param>
    public DeleteUserResponsePacket(DeleteUserStatus status, string username) : base(0x03, 0)
    {
        Writer.Write((byte)0x06);
        Writer.Write((byte)status);
        Writer.WriteStringNull(username);
    }
}

/// <summary>
/// Sends the complete configured user list to an administrator.
/// </summary>
public class UserListPacket : Packet
{
    /// <summary>
    /// Initializes a user-list packet.
    /// </summary>
    /// <param name="ns">The requesting administrator session.</param>
    public UserListPacket(NetState<CEDServer> ns) : base(0x03, 0)
    {
        var accounts = ns.Parent.Config.Accounts;
        Writer.Write((byte)0x07);
        Writer.Write((ushort)accounts.Count);
        foreach (var account in accounts)
        {
            Writer.WriteStringNull(account.Name);
            Writer.Write((byte)account.AccessLevel);
            Writer.Write((byte)account.Regions.Count);
            foreach (var region in account.Regions)
            {
                Writer.WriteStringNull(region);
            }
        }
    }
}

/// <summary>
/// Reports the outcome of a region add or modify request.
/// </summary>
public class ModifyRegionResponsePacket : Packet
{
    /// <summary>
    /// Initializes a modify-region response packet.
    /// </summary>
    /// <param name="status">The result of the requested region mutation.</param>
    /// <param name="region">The resulting region definition.</param>
    public ModifyRegionResponsePacket(ModifyRegionStatus status, Region region) : base(0x03, 0)
    {
        Writer.Write((byte)0x08);
        Writer.Write((byte)status);
        Writer.WriteStringNull(region.Name);
        if (status is ModifyRegionStatus.Added or ModifyRegionStatus.Modified)
        {
            Writer.Write((byte)region.Area.Count);
            foreach (var rect in region.Area)
            {
                rect.Write(Writer);
            }
        }
    }
}

/// <summary>
/// Reports the outcome of a region deletion request.
/// </summary>
public class DeleteRegionResponsePacket : Packet
{
    /// <summary>
    /// Initializes a delete-region response packet.
    /// </summary>
    /// <param name="status">The result of the requested deletion.</param>
    /// <param name="regionName">The region name that was targeted.</param>
    public DeleteRegionResponsePacket(DeleteRegionStatus status, string regionName) : base(0x03, 0)
    {
        Writer.Write((byte)0x09);
        Writer.Write((byte)status);
        Writer.WriteStringNull(regionName);
    }
}

/// <summary>
/// Sends the complete configured region list to an administrator.
/// </summary>
public class RegionListPacket : Packet
{
    /// <summary>
    /// Initializes a region-list packet.
    /// </summary>
    /// <param name="ns">The requesting administrator session.</param>
    public RegionListPacket(NetState<CEDServer> ns) : base(0x03, 0)
    {
        var regions = ns.Parent.Config.Regions;
        Writer.Write((byte)0x0A);
        Writer.Write((byte)regions.Count);
        foreach (var region in regions)
        {
            Writer.WriteStringNull(region.Name);
            Writer.Write((byte)region.Area.Count);
            foreach (var rect in region.Area)
            {
                rect.Write(Writer);
            }
        }
    }
}