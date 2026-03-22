using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using CentrED.Client.Map;
using CentrED.Network;
using static CentrED.Client.AdminHandling;

namespace CentrED.Client;

/// <summary>
/// Represents a callback raised when the client establishes a session.
/// </summary>
public delegate void Connected();

/// <summary>
/// Represents a callback raised when the client disconnects.
/// </summary>
public delegate void Disconnected();

/// <summary>
/// Represents a callback raised when the client camera position changes.
/// </summary>
/// <param name="newX">The new tile X coordinate.</param>
/// <param name="newY">The new tile Y coordinate.</param>
public delegate void Moved(ushort newX, ushort newY);

/// <summary>
/// Represents a callback raised when another user connects or disconnects.
/// </summary>
/// <param name="used">The affected username.</param>
public delegate void ClientConnection(string used);

/// <summary>
/// Represents a callback raised when chat text is received.
/// </summary>
/// <param name="user">The sending username.</param>
/// <param name="message">The chat message text.</param>
public delegate void ChatMessage(string user, string message);

/// <summary>
/// Represents a callback raised when the client emits a log message.
/// </summary>
/// <param name="message">The log message text.</param>
public delegate void LogMessage(string message);

/// <summary>
/// Represents a user entry returned by the administrative API.
/// </summary>
/// <param name="Username">The username.</param>
/// <param name="AccessLevel">The granted access level.</param>
/// <param name="Regions">The regions assigned to the user.</param>
public record struct User(string Username, AccessLevel AccessLevel, List<string> Regions);

/// <summary>
/// Represents an editable region returned by the administrative API.
/// </summary>
/// <param name="Name">The region name.</param>
/// <param name="Areas">The rectangles that compose the region.</param>
public record struct Region(string Name, List<RectU16> Areas);

/// <summary>
/// Represents the complete administrative data snapshot cached by the client.
/// </summary>
/// <param name="Users">The configured users.</param>
/// <param name="Regions">The configured regions.</param>
public record struct Admin(List<User> Users, List<Region> Regions);

/// <summary>
/// Describes the connection lifecycle state of the client.
/// </summary>
public enum ClientState
{
    Error,
    Disconnected,
    Connected,
    Running,
}

/// <summary>
/// Hosts the client connection, local landscape cache, undo stack, and event surface used by the editor.
/// </summary>
public sealed class CentrEDClient : ILogging
{
    private const int RecvPipeSize = 1024 * 256;
    private NetState<CentrEDClient>? NetState { get; set; }
    private ClientLandscape? Landscape { get; set; }

    /// <summary>
    /// Gets a value indicating whether the session negotiated the CentrED+ protocol.
    /// </summary>
    public bool CentrEdPlus { get; internal set; }

    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    public ClientState State { get; internal set; } = ClientState.Disconnected;

    /// <summary>
    /// Gets the current coarse-grained server state.
    /// </summary>
    public ServerState ServerState { get; internal set; } = ServerState.Running;

    /// <summary>
    /// Gets the optional server-state reason string.
    /// </summary>
    public string ServerStateReason { get; internal set; } = "";

    /// <summary>
    /// Gets the connected server hostname.
    /// </summary>
    public string Hostname { get; private set; }

    /// <summary>
    /// Gets the connected server port.
    /// </summary>
    public int Port { get; private set; }

    /// <summary>
    /// Gets the authenticated username.
    /// </summary>
    public string? Username => NetState?.Username;

    /// <summary>
    /// Gets the cached password used for reconnect-sensitive operations.
    /// </summary>
    public string? Password { get; private set; }

    /// <summary>
    /// Gets the effective access level granted by the server.
    /// </summary>
    public AccessLevel AccessLevel { get; internal set; }

    /// <summary>
    /// Gets the current tile X position.
    /// </summary>
    public ushort X { get; private set; }

    /// <summary>
    /// Gets the current tile Y position.
    /// </summary>
    public ushort Y { get; private set; }

    /// <summary>
    /// Gets the undo stack of packet groups.
    /// </summary>
    public Stack<Packet[]> UndoStack { get; private set; } = new();

    /// <summary>
    /// Gets the redo stack of packet groups.
    /// </summary>
    public Stack<Packet[]> RedoStack { get; private set; } = new();
    internal List<Packet>? UndoGroup;
    
    internal Queue<PointU16> RequestedBlocksQueue = new();
    internal HashSet<PointU16> RequestedBlocks = [];

    /// <summary>
    /// Gets the currently connected usernames.
    /// </summary>
    public List<String> Clients { get; } = new();

    /// <summary>
    /// Gets a value indicating whether the client is fully running.
    /// </summary>
    public bool Running => State == ClientState.Running;

    /// <summary>
    /// Gets the latest human-readable client status text.
    /// </summary>
    public string Status { get; internal set; } = "";
    internal TileDataLand[]? LandTileData;
    internal TileDataStatic[]? StaticTileData;

    /// <summary>
    /// Gets the cached administrative snapshot.
    /// </summary>
    public Admin Admin = new([], []);

    /// <summary>
    /// Resets all local connection and cache state.
    /// </summary>
    private void Reset()
    {
        NetState?.Dispose();
        NetState = null;
        Landscape = null;
        Hostname = "";
        Port = 0;
        Password = "";
        AccessLevel = AccessLevel.None;
        X = 0;
        Y = 0;
        UndoStack.Clear();
        UndoGroup = null;
        RequestedBlocksQueue.Clear();
        RequestedBlocks.Clear();
        Clients.Clear();
        State = ClientState.Disconnected;
        ServerState = ServerState.Running;
        Status = "";
        Admin = new Admin([],[]);
    }

    /// <summary>
    /// Registers top-level packet handlers for a connected session.
    /// </summary>
    /// <param name="ns">The connected network state.</param>
    private void RegisterPacketHandlers(NetState<CentrEDClient> ns)
    {
        ns.RegisterPacketHandler(0x01, 0, Zlib.OnCompressedPacket);
        ns.RegisterPacketHandler(0x02, 0, ConnectionHandling.OnConnectionHandlerPacket);
        ns.RegisterPacketHandler(0x03, 0, AdminHandling.OnAdminHandlerPacket);
        ns.RegisterPacketHandler(0x0C, 0, ClientHandling.OnClientHandlerPacket);
        ns.RegisterPacketHandler(0x0D, 0, RadarMap.OnRadarHandlerPacket);
    }

    /// <summary>
    /// Connects to a CentrED server and performs the login handshake.
    /// </summary>
    /// <param name="hostname">The server hostname.</param>
    /// <param name="port">The server port.</param>
    /// <param name="username">The login username.</param>
    /// <param name="password">The login password.</param>
    public void Connect(string hostname, int port, string username, string password)
    {
        Reset();
        Hostname = hostname;
        Port = port;
        Password = password;
        var ipAddress = Dns.GetHostAddresses(hostname)[0];
        var ipEndPoint = new IPEndPoint(ipAddress, port);
        var socket = new Socket(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        socket.Connect(ipEndPoint);
        State = ClientState.Connected;
        NetState = new NetState<CentrEDClient>(this, socket, recvPipeSize: RecvPipeSize);
        RegisterPacketHandlers(NetState);
        NetState.Username = username;
        NetState.Send(new LoginRequestPacket(username, password));
        NetState.Flush();
        
        do
        {
            NetState.Receive();
        } while (State == ClientState.Connected);
    }

    /// <summary>
    /// Supplies tiledata arrays used for local map validation and sorting.
    /// </summary>
    /// <param name="landTileData">The land tile data.</param>
    /// <param name="staticTileData">The static tile data.</param>
    public void InitTileData(TileDataLand[] landTileData, TileDataStatic[] staticTileData)
    {
        LandTileData = landTileData;
        StaticTileData = staticTileData;
    }

    /// <summary>
    /// Disconnects from the server, requesting a clean quit when possible.
    /// </summary>
    public void Disconnect()
    {
        if (Running)
        {
            Send(new QuitPacket());
            while (NetState.FlushPending)
            {
                NetState.Flush();
            }
            while (NetState.Receive())
            {
                //Wait for QuitAckPacket
            }
        }
        else
        {
            Shutdown();
        }
    }
    
    /// <summary>
    /// Tears down the local session immediately.
    /// </summary>
    public void Shutdown()
    {
        NetState?.Disconnect();
        Landscape = null;
        State = ClientState.Disconnected;
        if (NetState != null)
        {
            while (NetState.FlushPending)
                NetState.Flush();
            NetState.Dispose();
        }
        Disconnected?.Invoke();
        Status = "Disconnected";
    }

    /// <summary>
    /// Services network traffic, block requests, and keep-alive packets.
    /// </summary>
    public void Update()
    {
        if (!Running)
            throw new Exception("Client not connected");
        try
        {
            if (DateTime.UtcNow - TimeSpan.FromSeconds(30) > NetState.LastAction)
            {
                Send(new NoOpPacket());
            }
            UpdateRequestedBlocks();
            
            if (NetState.FlushPending)
            {
                if (!NetState.Flush())
                {
                    Disconnect();
                    State = ClientState.Error;
                }
            }

            if (!NetState.Receive())
            {
                State = ClientState.Error;
            }
        }
        catch(Exception e)
        {
            Shutdown();
            State = ClientState.Error;
            throw;
        }
    }

    /// <summary>
    /// Gets the map width in blocks.
    /// </summary>
    public ushort Width => Landscape?.Width ?? 0;

    /// <summary>
    /// Gets the map height in blocks.
    /// </summary>
    public ushort Height => Landscape?.Height ?? 0;

    /// <summary>
    /// Gets the map width in tiles.
    /// </summary>
    public ushort WidthInTiles => Landscape?.WidthInTiles ?? 0;

    /// <summary>
    /// Gets the map height in tiles.
    /// </summary>
    public ushort HeightInTiles => Landscape?.HeightInTiles ?? 0;

    /// <summary>
    /// Loads all blocks needed for the supplied area and waits until they arrive.
    /// </summary>
    /// <param name="areaInfo">The requested tile rectangle.</param>
    public void LoadBlocks(RectU16 areaInfo)
    {
        RequestBlocks(areaInfo);
        while (WaitingForBlocks)
        {
            Update();
        }
    }

    /// <summary>
    /// Queues block requests for the supplied area.
    /// </summary>
    /// <param name="areaInfo">The requested tile rectangle.</param>
    public void RequestBlocks(RectU16 areaInfo)
    {
        List<PointU16> toRequest = new List<PointU16>();
        foreach (var (x,y) in (areaInfo / 8).Iterate())
        {
            if(!IsValidX(x) || !IsValidY(y))
                continue;
            if(Landscape.BlockCache.Contains(Block.Id(x, y)))
                continue;
            
            var chunk = new PointU16(x, y);
            if(RequestedBlocks.Contains(chunk))
                continue;
            
            toRequest.Add(chunk);
        }
      
        Landscape.BlockCache.Grow(Math.Max(1, areaInfo.Width * areaInfo.Height / 8));
        
        toRequest.ForEach(b => RequestedBlocks.Add(b));
        toRequest.ForEach(b => RequestedBlocksQueue.Enqueue(b));;
    }

    /// <summary>
    /// Sends queued block requests in batches.
    /// </summary>
    private void UpdateRequestedBlocks()
    {
        if (RequestedBlocksQueue.Count > 0)
        {
            var blocksCount = Math.Min(RequestedBlocksQueue.Count, 1000);
            var packet = new RequestBlocksPacket(Enumerable.Range(0, blocksCount).Select(_ => RequestedBlocksQueue.Dequeue()));
            if (blocksCount > 20)
            {
                SendCompressed(packet);
            }
            else
            {
                Send(packet);
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether any requested blocks are still outstanding.
    /// </summary>
    public bool WaitingForBlocks => RequestedBlocks.Count > 0;

    /// <summary>
    /// Determines whether an X coordinate is within map bounds.
    /// </summary>
    /// <param name="x">The tile X coordinate.</param>
    /// <returns><see langword="true"/> when the coordinate is valid.</returns>
    public bool IsValidX(int x)
    {
        return x >= 0 && x < WidthInTiles;
    }

    /// <summary>
    /// Clamps an X coordinate into map bounds.
    /// </summary>
    /// <param name="x">The tile X coordinate.</param>
    /// <returns>The clamped coordinate.</returns>
    public ushort ClampX(int x)
    {
        return (ushort)Math.Clamp(x, 0, WidthInTiles - 1);
    }

    /// <summary>
    /// Determines whether a Y coordinate is within map bounds.
    /// </summary>
    /// <param name="y">The tile Y coordinate.</param>
    /// <returns><see langword="true"/> when the coordinate is valid.</returns>
    public bool IsValidY(int y)
    {
        return y >= 0 && y < HeightInTiles;
    }
    
    /// <summary>
    /// Clamps a Y coordinate into map bounds.
    /// </summary>
    /// <param name="y">The tile Y coordinate.</param>
    /// <returns>The clamped coordinate.</returns>
    public ushort ClampY(int y)
    {
        return (ushort)Math.Clamp(y, 0, HeightInTiles - 1);
    }
    
    /// <summary>
    /// Updates the cached client position without raising movement events.
    /// </summary>
    /// <param name="x">The destination tile X coordinate.</param>
    /// <param name="y">The destination tile Y coordinate.</param>
    /// <returns><see langword="true"/> when the position changed.</returns>
    public bool InternalSetPos(ushort x, ushort y)
    {
        if (x == X && y == Y)
            return false;
        if(!IsValidX(x) || !IsValidY(y))
            return false;

        // Only notify the server when the client crossed into a different block.
        if(Landscape.TileBlockIndex(x,y) != Landscape.TileBlockIndex(X,Y))
        {
            Send(new UpdateClientPosPacket(x, y));
        }
        X = x;
        Y = y;
        return true;
    }
    
    /// <summary>
    /// Updates the client position and raises the movement event when needed.
    /// </summary>
    /// <param name="x">The destination tile X coordinate.</param>
    /// <param name="y">The destination tile Y coordinate.</param>
    public void SetPos(ushort x, ushort y)
    {
        if(InternalSetPos(x, y))
        {
            Moved?.Invoke(x, y);
        }
    }
    
    /// <summary>
    /// Gets a land tile from the local landscape cache.
    /// </summary>
    /// <param name="x">The tile X coordinate.</param>
    /// <param name="y">The tile Y coordinate.</param>
    /// <returns>The requested land tile.</returns>
    public LandTile GetLandTile(int x, int y)
    {
        return Landscape.GetLandTile(Convert.ToUInt16(x), Convert.ToUInt16(y));
    }
    
    /// <summary>
    /// Tries to get a land tile from the local landscape cache.
    /// </summary>
    /// <param name="x">The tile X coordinate.</param>
    /// <param name="y">The tile Y coordinate.</param>
    /// <param name="landTile">The requested land tile when present.</param>
    /// <returns><see langword="true"/> when the tile exists.</returns>
    public bool TryGetLandTile(int x, int y, [MaybeNullWhen(false)] out LandTile landTile)
    {
        if (!IsValidX(x) || !IsValidY(y))
        {
            landTile = null;
            return false;
        }
        return Landscape.TryGetLandTile(Convert.ToUInt16(x), Convert.ToUInt16(y), out landTile);
    }

    /// <summary>
    /// Gets static tiles from the local landscape cache.
    /// </summary>
    /// <param name="x">The tile X coordinate.</param>
    /// <param name="y">The tile Y coordinate.</param>
    /// <returns>The static tiles at the requested location.</returns>
    public IEnumerable<StaticTile> GetStaticTiles(int x, int y)
    {
        return Landscape.GetStaticTiles(Convert.ToUInt16(x), Convert.ToUInt16(y));
    }
    
    /// <summary>
    /// Tries to get static tiles from the local landscape cache.
    /// </summary>
    /// <param name="x">The tile X coordinate.</param>
    /// <param name="y">The tile Y coordinate.</param>
    /// <param name="staticTiles">The static tiles when present.</param>
    /// <returns><see langword="true"/> when the location is valid.</returns>
    public bool TryGetStaticTiles(int x, int y, [MaybeNullWhen(false)] out IEnumerable<StaticTile> staticTiles)
    {
        if (!IsValidX(x) || !IsValidY(y))
        {
            staticTiles = Enumerable.Empty<StaticTile>();
            return false;
        }
        return Landscape.TryGetStaticTiles(Convert.ToUInt16(x), Convert.ToUInt16(y), out staticTiles);
    }

    /// <summary>
    /// Adds a static tile through the local landscape mirror.
    /// </summary>
    /// <param name="tile">The static tile to add.</param>
    public void Add(StaticTile tile)
    {
        Landscape.AddTile(tile);
    }

    /// <summary>
    /// Removes a static tile through the local landscape mirror.
    /// </summary>
    /// <param name="tile">The static tile to remove.</param>
    public void Remove(StaticTile tile)
    {
        Landscape.RemoveTile(tile);
    }
    
    /// <summary>
    /// Sends a raw packet.
    /// </summary>
    /// <param name="p">The packet to send.</param>
    public void Send(Packet p)
    {
        NetState.Send(p);
        NetState.LastAction = DateTime.UtcNow;
    }

    /// <summary>
    /// Sends a raw byte payload.
    /// </summary>
    /// <param name="data">The payload to send.</param>
    public void Send(ReadOnlySpan<byte> data)
    {
        NetState.Send(data);
        NetState.LastAction = DateTime.UtcNow;
    }

    /// <summary>
    /// Sends a packet using zlib compression.
    /// </summary>
    /// <param name="p">The packet to send.</param>
    public void SendCompressed(Packet p)
    {
        NetState.SendCompressed(p);
        NetState.LastAction = DateTime.UtcNow;
    }

    /// <summary>
    /// Sends a packet and records its inverse in the undo stack.
    /// </summary>
    /// <param name="p">The packet to send.</param>
    public void SendWithUndo(Packet p)
    {
        PushUndoPacket(GetUndoPacket(p));
        Send(p);
    }

    /// <summary>
    /// Resets the local block cache sizing policy.
    /// </summary>
    public void ResetCache()
    {
        Landscape?.BlockCache.Reset();
        Landscape?.BlockCache.Resize(Math.Max(Width, Height) + 1);
    }

    /// <summary>
    /// Requests an immediate server flush.
    /// </summary>
    public void Flush()
    {
        NetState.Send(new ServerFlushPacket());
    }
    
    /// <summary>
    /// Starts a packet-based undo group.
    /// </summary>
    /// <returns><see langword="true"/> when a new group was started.</returns>
    public bool BeginUndoGroup()
    {
        if (UndoGroup != null)
        {
            return false; //Group already opened, 
        }
        UndoGroup = new List<Packet>();
        return true;
    }

    /// <summary>
    /// Ends the active undo group and pushes it onto the undo stack.
    /// </summary>
    public void EndUndoGroup()
    {
        if (UndoGroup?.Count > 0)
        {
            UndoStack.Push(UndoGroup.ToArray());
        }
        UndoGroup = null;
    }

    /// <summary>
    /// Adds an inverse packet to the current undo target.
    /// </summary>
    /// <param name="p">The inverse packet.</param>
    internal void PushUndoPacket(Packet? p)
    {
        if (p == null)
            return;
        
        if (UndoGroup != null)
        {
            UndoGroup.Add(p);
        }
        else
        {
            UndoStack.Push([p]);
        }
    }

    /// <summary>
    /// Gets a value indicating whether an undo operation is available.
    /// </summary>
    public bool CanUndo => UndoStack.Count > 0;
    
    /// <summary>
    /// Replays the most recent undo packet group.
    /// </summary>
    public void Undo()
    {
        if (UndoStack.Count > 0)
        {
            var undoList = UndoStack.Pop();
            var redoList = new List<Packet>(undoList.Length);
            foreach (var packet in undoList.Reverse())
            {
                redoList.Add(GetUndoPacket(packet)!); //If we can UnDo, we can always ReDo
                Send(packet);
            }
            RedoStack.Push(redoList.ToArray());
        }
    }
    
    /// <summary>
    /// Gets a value indicating whether a redo operation is available.
    /// </summary>
    public bool CanRedo => RedoStack.Count > 0;

    /// <summary>
    /// Replays the most recent redo packet group.
    /// </summary>
    public void Redo()
    {
        if (RedoStack.Count > 0)
        {
            var redoList = RedoStack.Pop();
            BeginUndoGroup();
            foreach (var packet in redoList.Reverse())
            {
                SendWithUndo(packet);
            }
            EndUndoGroup();
        }
    }

    /// <summary>
    /// Clears the redo stack after a new mutating action.
    /// </summary>
    public void ClearRedo()
    {
        RedoStack.Clear();
    }

    /// <summary>
    /// Builds the inverse packet for a supported mutating packet.
    /// </summary>
    /// <param name="packet">The packet to invert.</param>
    /// <returns>The inverse packet, or <see langword="null"/> when not supported.</returns>
    public Packet? GetUndoPacket(Packet packet)
    {
        Packet? undoPacket = null;
        if (packet is DrawMapPacket drawMapPacket)
        {
            var landTile = GetLandTile(drawMapPacket.X, drawMapPacket.Y);
            undoPacket = new DrawMapPacket(landTile);
        }
        else if (packet is InsertStaticPacket isp)
        {
            undoPacket = new DeleteStaticPacket(isp.X, isp.Y, isp.Z, isp.TileId, isp.Hue);
        }
        else if (packet is DeleteStaticPacket dsp)
        {
            undoPacket = new InsertStaticPacket(dsp.X, dsp.Y, dsp.Z, dsp.TileId, dsp.Hue);
        }
        else if (packet is ElevateStaticPacket esp)
        {
            undoPacket = new ElevateStaticPacket(esp.X, esp.Y, esp.NewZ, esp.TileId, esp.Hue, esp.Z);
        }
        else if (packet is MoveStaticPacket msp)
        {
            undoPacket = new MoveStaticPacket(msp.NewX, msp.NewY, msp.Z, msp.TileId, msp.Hue, msp.X, msp.Y);
        }
        else if (packet is HueStaticPacket hsp)
        {
            undoPacket = new HueStaticPacket(hsp.X, hsp.Y, hsp.Z, hsp.TileId, hsp.NewHue, hsp.Hue);
        }
        return undoPacket;
    }
    
    /// <summary>
    /// Initializes the local landscape after a successful login handshake.
    /// </summary>
    /// <param name="width">The map width in blocks.</param>
    /// <param name="height">The map height in blocks.</param>
    internal void InitLandscape(ushort width, ushort height)
    {
        Landscape = new ClientLandscape(this, width, height);
        Landscape.RegisterPacketHandlers(NetState!);
        ResetCache();
        Connected?.Invoke();
        State = ClientState.Running;
        if (AccessLevel == AccessLevel.Administrator)
        {
            Send(new ListUsersPacket());
            Send(new ListRegionsPacket());
        }
    }

    #region events

    /// <summary>
    /// Raised when the client establishes a running session.
    /// </summary>

    public event Connected? Connected;
    /// <summary>
    /// Raised when the client disconnects.
    /// </summary>
    public event Disconnected? Disconnected;
    /// <summary>
    /// Raised after any map-visible change.
    /// </summary>
    public event MapChanged? MapChanged;
    /// <summary>
    /// Raised when a block leaves the cache.
    /// </summary>
    public event BlockChanged? BlockUnloaded;
    /// <summary>
    /// Raised when a block enters the cache.
    /// </summary>
    public event BlockChanged? BlockLoaded;
    /// <summary>
    /// Raised when a land tile is replaced.
    /// </summary>
    public event LandReplaced? LandTileReplaced;
    /// <summary>
    /// Raised when a land tile is elevated.
    /// </summary>
    public event LandElevated? LandTileElevated;
    /// <summary>
    /// Raised when a static tile is added.
    /// </summary>
    public event StaticChanged? StaticTileAdded;
    /// <summary>
    /// Raised when a static tile is removed.
    /// </summary>
    public event StaticChanged? StaticTileRemoved;
    /// <summary>
    /// Raised when a static tile id changes.
    /// </summary>
    public event StaticReplaced? StaticTileReplaced;
    /// <summary>
    /// Raised when a static tile moves.
    /// </summary>
    public event StaticMoved? StaticTileMoved;
    /// <summary>
    /// Raised when a static tile altitude changes.
    /// </summary>
    public event StaticElevated? StaticTileElevated;
    /// <summary>
    /// Raised after any static mutation completes.
    /// </summary>
    public event StaticChanged? AfterStaticChanged;
    /// <summary>
    /// Raised when a static tile hue changes.
    /// </summary>
    public event StaticHued? StaticTileHued;
    /// <summary>
    /// Raised when another client connects.
    /// </summary>
    public event ClientConnection? ClientConnected;
    /// <summary>
    /// Raised when another client disconnects.
    /// </summary>
    public event ClientConnection? ClientDisconnected;
    /// <summary>
    /// Raised when the camera position changes.
    /// </summary>
    public event Moved? Moved;
    /// <summary>
    /// Raised when a chat message arrives.
    /// </summary>
    public event ChatMessage? ChatMessage;
    /// <summary>
    /// Raised when a radar checksum arrives.
    /// </summary>
    public event RadarChecksum? RadarChecksum;
    /// <summary>
    /// Raised when a full radar image arrives.
    /// </summary>
    public event RadarData? RadarData;
    /// <summary>
    /// Raised when a single radar pixel update arrives.
    /// </summary>
    public event RadarUpdate? RadarUpdate;
    /// <summary>
    /// Raised when a user definition changes.
    /// </summary>
    public event UserModified? UserModified;
    /// <summary>
    /// Raised when a user definition is deleted.
    /// </summary>
    public event UserDeleted? UserDeleted;
    /// <summary>
    /// Raised when a region definition changes.
    /// </summary>
    public event RegionModified? RegionModified;
    /// <summary>
    /// Raised when a region definition is deleted.
    /// </summary>
    public event RegionDeleted? RegionDeleted;
    /// <summary>
    /// Raised when an informational log message is emitted.
    /// </summary>
    public event LogMessage? LoggedInfo;
    /// <summary>
    /// Raised when a warning log message is emitted.
    /// </summary>
    public event LogMessage? LoggedWarn;
    /// <summary>
    /// Raised when an error log message is emitted.
    /// </summary>
    public event LogMessage? LoggedError;
    /// <summary>
    /// Raised when a debug log message is emitted.
    /// </summary>
    public event LogMessage? LoggedDebug;
    
    /// <summary>
    /// Raises the map-changed event.
    /// </summary>
    internal void OnMapChanged()
    {
        MapChanged?.Invoke();
    }

    /// <summary>
    /// Raises the block-unloaded event.
    /// </summary>
    /// <param name="block">The released block.</param>
    internal void OnBlockReleased(Block block)
    {
        BlockUnloaded?.Invoke(block);
        OnMapChanged();
    }

    /// <summary>
    /// Raises the block-loaded event.
    /// </summary>
    /// <param name="block">The loaded block.</param>
    internal void OnBlockLoaded(Block block)
    {
        BlockLoaded?.Invoke(block);
        OnMapChanged();
    }

    /// <summary>
    /// Raises the land-replaced event.
    /// </summary>
    /// <param name="landTile">The affected land tile.</param>
    /// <param name="newId">The new land tile id.</param>
    /// <param name="newZ">The new altitude.</param>
    internal void OnLandReplaced(LandTile landTile, ushort newId, sbyte newZ)
    {
        LandTileReplaced?.Invoke(landTile, newId, newZ);
        OnMapChanged();
    }

    /// <summary>
    /// Raises the land-elevated event.
    /// </summary>
    /// <param name="landTile">The affected land tile.</param>
    /// <param name="newZ">The new altitude.</param>
    internal void OnLandElevated(LandTile landTile, sbyte newZ)
    {
        LandTileElevated?.Invoke(landTile, newZ);
        OnMapChanged();
    }

    /// <summary>
    /// Raises the static-added event.
    /// </summary>
    /// <param name="staticTile">The added static tile.</param>
    internal void OnStaticTileAdded(StaticTile staticTile)
    {
        StaticTileAdded?.Invoke(staticTile);
        OnMapChanged();
    }

    /// <summary>
    /// Raises the static-removed event.
    /// </summary>
    /// <param name="staticTile">The removed static tile.</param>
    internal void OnStaticTileRemoved(StaticTile staticTile)
    {
        StaticTileRemoved?.Invoke(staticTile);
        OnMapChanged();
    }

    /// <summary>
    /// Raises the static-replaced event.
    /// </summary>
    /// <param name="staticTile">The affected static tile.</param>
    /// <param name="newId">The new static id.</param>
    internal void OnStaticTileReplaced(StaticTile staticTile, ushort newId)
    {
        StaticTileReplaced?.Invoke(staticTile, newId);
        OnMapChanged();
    }

    /// <summary>
    /// Raises the static-moved event.
    /// </summary>
    /// <param name="staticTile">The moved static tile.</param>
    /// <param name="newX">The new X coordinate.</param>
    /// <param name="newY">The new Y coordinate.</param>
    internal void OnStaticTileMoved(StaticTile staticTile, ushort newX, ushort newY)
    {
        StaticTileMoved?.Invoke(staticTile, newX, newY);
        OnMapChanged();
    }

    /// <summary>
    /// Raises the static-elevated event.
    /// </summary>
    /// <param name="staticTile">The affected static tile.</param>
    /// <param name="newZ">The new altitude.</param>
    internal void OnStaticTileElevated(StaticTile staticTile, sbyte newZ)
    {
        StaticTileElevated?.Invoke(staticTile, newZ);
        OnMapChanged();
    }
    
    /// <summary>
    /// Raises the after-static-changed event.
    /// </summary>
    /// <param name="staticTile">The affected static tile.</param>
    internal void OnAfterStaticChanged(StaticTile staticTile)
    {
        AfterStaticChanged?.Invoke(staticTile);
        OnMapChanged();
    }

    /// <summary>
    /// Raises the static-hued event.
    /// </summary>
    /// <param name="staticTile">The affected static tile.</param>
    /// <param name="newHue">The new hue.</param>
    internal void OnStaticTileHued(StaticTile staticTile, ushort newHue)
    {
        StaticTileHued?.Invoke(staticTile, newHue);
        OnMapChanged();
    }

    /// <summary>
    /// Raises the client-connected event.
    /// </summary>
    /// <param name="user">The connected username.</param>
    internal void OnClientConnected(string user)
    {
        ClientConnected?.Invoke(user);   
    }
    
    /// <summary>
    /// Raises the client-disconnected event.
    /// </summary>
    /// <param name="user">The disconnected username.</param>
    internal void OnClientDisconnected(string user)
    {
        ClientDisconnected?.Invoke(user);
    }

    /// <summary>
    /// Raises the chat-message event.
    /// </summary>
    /// <param name="user">The sending username.</param>
    /// <param name="message">The message text.</param>
    internal void OnChatMessage(string user, string message)
    {
        ChatMessage?.Invoke(user, message);
    }

    /// <summary>
    /// Raises the radar-checksum event.
    /// </summary>
    /// <param name="checksum">The checksum value.</param>
    internal void OnRadarChecksum(uint checksum)
    {
        RadarChecksum?.Invoke(checksum);
    }

    /// <summary>
    /// Raises the full-radar-data event.
    /// </summary>
    /// <param name="data">The radar payload.</param>
    internal void OnRadarData(ReadOnlySpan<ushort> data)
    {
        RadarData?.Invoke(data);
    }

    /// <summary>
    /// Raises the radar-update event.
    /// </summary>
    /// <param name="x">The radar X coordinate.</param>
    /// <param name="y">The radar Y coordinate.</param>
    /// <param name="color">The new radar color.</param>
    internal void OnRadarUpdate(ushort x, ushort y, ushort color)
    {
        RadarUpdate?.Invoke(x, y, color);
    }

    /// <summary>
    /// Raises the user-modified event.
    /// </summary>
    /// <param name="username">The affected username.</param>
    /// <param name="status">The modification status.</param>
    internal void OnUserModified(string username, ModifyUserStatus status)
    {
        UserModified?.Invoke(username, status);
    }
    
    /// <summary>
    /// Raises the user-deleted event.
    /// </summary>
    /// <param name="username">The deleted username.</param>
    internal void OnUserDeleted(string username)
    {
        UserDeleted?.Invoke(username);
    }
    
    /// <summary>
    /// Raises the region-modified event.
    /// </summary>
    /// <param name="name">The affected region name.</param>
    /// <param name="status">The modification status.</param>
    internal void OnRegionModified(string name, ModifyRegionStatus status)
    {
        RegionModified?.Invoke(name, status);
    }
    
    /// <summary>
    /// Raises the region-deleted event.
    /// </summary>
    /// <param name="name">The deleted region name.</param>
    internal void OnRegionDeleted(string name)
    {
        RegionDeleted?.Invoke(name);
    }

    /// <summary>
    /// Emits an informational log message.
    /// </summary>
    /// <param name="message">The log message.</param>
    public void LogInfo(string message)
    {
        LoggedInfo?.Invoke(message);
    }

    /// <summary>
    /// Emits a warning log message.
    /// </summary>
    /// <param name="message">The log message.</param>
    public void LogWarn(string message)
    {
        LoggedWarn?.Invoke(message);
    }

    /// <summary>
    /// Emits an error log message.
    /// </summary>
    /// <param name="message">The log message.</param>
    public void LogError(string message)
    {
        LoggedError?.Invoke(message);
    }

    /// <summary>
    /// Emits a debug log message.
    /// </summary>
    /// <param name="message">The log message.</param>
    public void LogDebug(string message)
    {
        LoggedDebug?.Invoke(message);
    }

    #endregion
}