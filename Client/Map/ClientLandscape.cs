using CentrED.Network;

namespace CentrED.Client.Map;

/// <summary>
/// Mirrors the server landscape locally and translates local mutations into outgoing packets.
/// </summary>
public partial class ClientLandscape : BaseLandscape
{
    private readonly CentrEDClient _client;
    
    /// <summary>
    /// Initializes a local landscape mirror for the supplied map dimensions.
    /// </summary>
    /// <param name="client">The owning client.</param>
    /// <param name="width">The map width in blocks.</param>
    /// <param name="height">The map height in blocks.</param>
    public ClientLandscape(CentrEDClient client, ushort width, ushort height) : base(width, height)
    {
        _client = client;
        // Local landscape events are mirrored into outbound edit packets for undo-aware editing.
        BlockUnloaded += OnBlockUnloaded;

        LandTileReplaced += OnLandTileReplaced;
        LandTileElevated += OnLandTileElevated;

        StaticTileAdded += OnStaticAdded;
        StaticTileRemoved += OnStaticRemoved;
        StaticTileReplaced += OnStaticReplaced;
        StaticTileMoved += OnStaticMoved;
        StaticTileElevated += OnStaticElevated;
        StaticTileHued += OnStaticHued;
    }

    /// <summary>
    /// Registers landscape packet handlers for a connected session.
    /// </summary>
    /// <param name="ns">The connected network state.</param>
    public void RegisterPacketHandlers(NetState<CentrEDClient> ns)
    {
        ns.RegisterPacketHandler(0x04, 0, OnBlockPacket);
        ns.RegisterPacketHandler(0x06, 8, OnDrawMapPacket);
        ns.RegisterPacketHandler(0x07, 10, OnInsertStaticPacket);
        ns.RegisterPacketHandler(0x08, 10, OnDeleteStaticPacket);
        ns.RegisterPacketHandler(0x09, 11, OnElevateStaticPacket);
        ns.RegisterPacketHandler(0x0A, 14, OnMoveStaticPacket);
        ns.RegisterPacketHandler(0x0B, 12, OnHueStaticPacket);
    }

    /// <summary>
    /// Handles a block leaving the cache and informs the server when the subscription can be released.
    /// </summary>
    /// <param name="block">The unloaded block.</param>
    private void OnBlockUnloaded(Block block)
    {
        _client.OnBlockReleased(block);
        if (block.Disposed)
        {
            _client.Send(new FreeBlockPacket(block.LandBlock.X, block.LandBlock.Y));
        }
        else
        {
            //Not disposed because still used, put it back
            BlockCache.Add(block);
        }
    }

    /// <summary>
    /// Sends a land replacement edit.
    /// </summary>
    /// <param name="tile">The affected tile.</param>
    /// <param name="newId">The new land tile id.</param>
    /// <param name="newZ">The new altitude.</param>
    private void OnLandTileReplaced(LandTile tile, ushort newId, sbyte newZ)
    {
        _client.SendWithUndo(new DrawMapPacket(tile, newId, newZ));
        _client.ClearRedo();
    }

    /// <summary>
    /// Sends a land elevation edit.
    /// </summary>
    /// <param name="tile">The affected tile.</param>
    /// <param name="newZ">The new altitude.</param>
    private void OnLandTileElevated(LandTile tile, sbyte newZ)
    {
        _client.SendWithUndo(new DrawMapPacket(tile, newZ));
        _client.ClearRedo();
    }

    /// <summary>
    /// Sends a static insertion edit.
    /// </summary>
    /// <param name="tile">The inserted static tile.</param>
    private void OnStaticAdded(StaticTile tile)
    {
        _client.SendWithUndo(new InsertStaticPacket(tile));
        _client.ClearRedo();
    }

    /// <summary>
    /// Sends a static removal edit.
    /// </summary>
    /// <param name="tile">The removed static tile.</param>
    private void OnStaticRemoved(StaticTile tile)
    {
        _client.SendWithUndo(new DeleteStaticPacket(tile));
        _client.ClearRedo();
    }

    /// <summary>
    /// Sends a static replacement as delete-plus-insert so undo remains explicit.
    /// </summary>
    /// <param name="tile">The affected static tile.</param>
    /// <param name="newId">The new static id.</param>
    private void OnStaticReplaced(StaticTile tile, ushort newId)
    {
        var shouldEndGroup = _client.BeginUndoGroup();
        _client.SendWithUndo(new DeleteStaticPacket(tile));
        _client.SendWithUndo(new InsertStaticPacket(tile.X, tile.Y, tile.Z, newId, tile.Hue));
        if (shouldEndGroup)
            _client.EndUndoGroup();
        _client.ClearRedo();
    }

    /// <summary>
    /// Sends a static move edit.
    /// </summary>
    /// <param name="tile">The moved static tile.</param>
    /// <param name="newX">The new X coordinate.</param>
    /// <param name="newY">The new Y coordinate.</param>
    private void OnStaticMoved(StaticTile tile, ushort newX, ushort newY)
    {
        _client.SendWithUndo(new MoveStaticPacket(tile, newX, newY));
        _client.ClearRedo();
    }

    /// <summary>
    /// Sends a static elevation edit.
    /// </summary>
    /// <param name="tile">The affected static tile.</param>
    /// <param name="newZ">The new altitude.</param>
    private void OnStaticElevated(StaticTile tile, sbyte newZ)
    {
        _client.SendWithUndo(new ElevateStaticPacket(tile, newZ));
        _client.ClearRedo();
    }

    /// <summary>
    /// Sends a static hue edit.
    /// </summary>
    /// <param name="tile">The affected static tile.</param>
    /// <param name="newHue">The new hue.</param>
    private void OnStaticHued(StaticTile tile, ushort newHue)
    {
        _client.SendWithUndo(new HueStaticPacket(tile, newHue));
        _client.ClearRedo();
    }

    /// <summary>
    /// Loads a block from the server into the local cache.
    /// </summary>
    /// <param name="x">The block X coordinate.</param>
    /// <param name="y">The block Y coordinate.</param>
    /// <returns>The loaded block.</returns>
    protected override Block LoadBlock(ushort x, ushort y)
    {
        AssertBlockCoords(x, y);
        _client.Send(new RequestBlocksPacket(new PointU16(x, y)));
        var blockId = Block.Id(x, y);
        var block = BlockCache.Get(blockId);
        while (_client.Running && block == null)
        {
            Thread.Sleep(1);
            _client.Update();
            block = BlockCache.Get(blockId);
        }

        return block;
    }

    /// <inheritdoc />
    public override void LogInfo(string message)
    {
        _client.LogInfo(message);
    }

    /// <inheritdoc />
    public override void LogWarn(string message)
    {
        _client.LogWarn(message);
    }

    /// <inheritdoc />
    public override void LogError(string message)
    {
        _client.LogError(message);
    }

    /// <inheritdoc />
    public override void LogDebug(string message)
    {
        _client.LogDebug(message);
    }
}