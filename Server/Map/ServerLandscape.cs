using System.Text;
using CentrED.Network;
using CentrED.Server.Config;
using CentrED.Utility;

namespace CentrED.Server.Map;

/// <summary>
/// Owns the server-side map storage, block cache, persistence, validation, and radar integration.
/// </summary>
public sealed partial class ServerLandscape : BaseLandscape, IDisposable, ILogging
{
    private readonly Logger _logger;

    /// <summary>
    /// Initializes the landscape from the configured map, statics, and metadata files.
    /// </summary>
    /// <param name="config">The server configuration that defines map dimensions and file paths.</param>
    /// <param name="logger">The logger used for status and validation output.</param>
    public ServerLandscape
    (
        ConfigRoot config,
        Logger logger
    ) : base(config.Map.Width, config.Map.Height)
    {
        _logger = logger;
        var mapFile = new FileInfo(config.Map.MapPath);
        if (!mapFile.Exists)
        {
            // Prompting here keeps first-run setup simple for standalone server use.
            Console.WriteLine("Map file not found, do you want to create it? [y/n]");
            if (Console.ReadLine() == "y")
            {
                InitMap(mapFile);
                mapFile = new FileInfo(config.Map.MapPath);
            }
        }
        if (mapFile.IsReadOnly)
        {
            throw new Exception($"{mapFile.Name} file is read-only");
        }
        _map = mapFile.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        _mapReader = new BinaryReader(_map, Encoding.UTF8);
        _mapWriter = new BinaryWriter(_map, Encoding.UTF8);
        IsUop = mapFile.Extension == ".uop";
        if (IsUop)
        {
            string uopPattern = mapFile.Name.Replace(mapFile.Extension, "").ToLowerInvariant();
            ReadUopFiles(uopPattern);
        }
        _logger.LogInfo($"Loaded {_map.Name}");

        var staidxFile = new FileInfo(config.Map.StaIdx);
        var staticsFile = new FileInfo(config.Map.Statics);
        if (!staidxFile.Exists && !staticsFile.Exists)
        {
            Console.WriteLine("Statics files not found, do you want to create them? [y/n]");
            if (Console.ReadLine() == "y")
            {
                InitStatics(staticsFile, staidxFile);
                staidxFile = new FileInfo(config.Map.StaIdx);
                staticsFile = new FileInfo(config.Map.Statics);
            }
        }
        if(!staidxFile.Exists)
            throw new Exception($"{staidxFile.Name} file not found");
        if(staidxFile.IsReadOnly)
            throw new Exception($"{staidxFile.Name} file is read-only");
        _staidx = staidxFile.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        _logger.LogInfo($"Loaded {staidxFile.Name}");
        _staidxReader = new BinaryReader(_staidx, Encoding.UTF8);
        _staidxWriter = new BinaryWriter(_staidx, Encoding.UTF8);
       
        if(!staticsFile.Exists)
            throw new Exception($"{staticsFile.Name} file not found");
        if(staticsFile.IsReadOnly)
            throw new Exception($"{staticsFile.Name} file is read-only");
        _statics = staticsFile.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        _logger.LogInfo($"Loaded {staticsFile.Name}");
        _staticsReader = new BinaryReader(_statics, Encoding.UTF8);
        _staticsWriter = new BinaryWriter(_statics, Encoding.UTF8);

        Validate();
        
        TileDataProvider = new TileDataProvider(config.Tiledata);
        _logger.LogInfo($"Loaded {config.Tiledata}");
        if (File.Exists(config.Hues))
        {
            if (HueProvider.GetHueCount(config.Hues, out HueCount))
            {
                _logger.LogInfo($"Loaded {config.Hues}: {HueCount} entries");
            }
            else
            {
                _logger.LogInfo($"{config.Hues} not found, using default hue count");
            }
        }
        else
        {
            _logger.LogInfo($"File {config.Hues} not found, using default hue count");
        }
        _radarMap = new RadarMap(this, _mapReader, _staidxReader, _staticsReader, config.Radarcol);
        _logger.LogInfo($"Loaded {config.Radarcol}");
        _logger.LogInfo("Creating Cache");
        BlockUnloaded += OnRemovedCachedObject;
        
        // Cache an entire strip of blocks so broad map scans do not thrash the disk.
        BlockCache.Resize(Math.Max(config.Map.Width, config.Map.Height) + 1);
    }

    /// <summary>
    /// Creates a new empty map file matching the configured dimensions.
    /// </summary>
    /// <param name="map">The map file to create.</param>
    private void InitMap(FileInfo map)
    {
        using var mapFile = map.Open(FileMode.CreateNew, FileAccess.Write);
        using var writer = new BinaryWriter(mapFile, Encoding.UTF8);
        var emptyBLock = LandBlock.Empty(this);
        writer.Seek(0, SeekOrigin.Begin);
        for (var x = 0; x < Width; x++)
        {
            for (var y = 0; y < Height; y++)
            {
                emptyBLock.Write(writer);
            }
        }
    }

    /// <summary>
    /// Creates empty statics and staidx files matching the configured dimensions.
    /// </summary>
    /// <param name="statics">The statics file to create.</param>
    /// <param name="staidx">The static index file to create.</param>
    private void InitStatics(FileInfo statics, FileInfo staidx)
    {
        using var staticsFile = statics.Open(FileMode.CreateNew, FileAccess.Write);
        using var staidxFile = staidx.Open(FileMode.CreateNew, FileAccess.Write);
        using var writer = new BinaryWriter(staidxFile, Encoding.UTF8);
        var emptyIndex = GenericIndex.Empty;
        writer.Seek(0, SeekOrigin.Begin);
        for (var x = 0; x < Width; x++)
        {
            for (var y = 0; y < Height; y++)
            {
                emptyIndex.Write(writer);
            }
        }
    }

    /// <summary>
    /// Finalizer that delegates to <see cref="Dispose(bool)"/> through the generated dispose pattern.
    /// </summary>
    ~ServerLandscape()
    {
        Dispose(false);
    }

    private readonly FileStream _map;
    private readonly FileStream _statics;
    private readonly FileStream _staidx;

    private readonly BinaryReader _mapReader;
    private readonly BinaryReader _staticsReader;
    private readonly BinaryReader _staidxReader;

    private readonly BinaryWriter _mapWriter;
    private readonly BinaryWriter _staticsWriter;
    private readonly BinaryWriter _staidxWriter;
    
    /// <summary>
    /// Gets a value indicating whether the backing map file uses the UOP container format.
    /// </summary>
    public bool IsUop { get; }

    /// <summary>
    /// Gets a value indicating whether the backing map file uses the classic MUL format.
    /// </summary>
    public bool IsMul => !IsUop;

    private UopFile[] UopFiles { get; set; } = null!;

    /// <summary>
    /// Gets the tiledata provider used to validate and sort land and static tiles.
    /// </summary>
    public TileDataProvider TileDataProvider { get; } = null!;
    private int HueCount = 3000;
    private RadarMap _radarMap = null!;

    /// <summary>
    /// Persists dirty land or static blocks when they leave the cache.
    /// </summary>
    /// <param name="block">The block being removed from the cache.</param>
    private void OnRemovedCachedObject(Block block)
    {
        if (block.LandBlock.Changed)
            SaveBlock(block.LandBlock);
        if (block.StaticBlock.Changed)
            SaveBlock(block.StaticBlock);
    }

    /// <summary>
    /// Validates a static tile identifier against the loaded tiledata.
    /// </summary>
    /// <param name="tileId">The static tile identifier to validate.</param>
    internal void AssertStaticTileId(ushort tileId)
    {
        if (tileId >= TileDataProvider.StaticTiles.Length)
            throw new ArgumentException($"Invalid static tile id {tileId}");
    }

    /// <summary>
    /// Validates a land tile identifier against the loaded tiledata.
    /// </summary>
    /// <param name="tileId">The land tile identifier to validate.</param>
    internal void AssertLandTileId(ushort tileId)
    {
        if (tileId >= TileDataProvider.LandTiles.Length)
            throw new ArgumentException($"Invalid land tile id {tileId}");
    }

    /// <summary>
    /// Validates a hue index against the loaded hue table.
    /// </summary>
    /// <param name="hue">The hue index to validate.</param>
    internal void AssertHue(ushort hue)
    {
        if (hue > HueCount)
            throw new ArgumentException($"Invalid hue {hue}");
    }
    
    /// <summary>
    /// Converts block coordinates into the flattened block index used by the server.
    /// </summary>
    /// <param name="x">The block X coordinate.</param>
    /// <param name="y">The block Y coordinate.</param>
    /// <returns>The flattened block number.</returns>
    public long GetBlockNumber(ushort x, ushort y)
    {
        return x * Height + y;
    }

    /// <summary>
    /// Gets the byte offset of a land block within the map storage.
    /// </summary>
    /// <param name="x">The block X coordinate.</param>
    /// <param name="y">The block Y coordinate.</param>
    /// <returns>The byte offset for the requested block.</returns>
    public long GetMapOffset(ushort x, ushort y)
    {
        long offset = GetBlockNumber(x, y) * 196;
        if (IsUop)
            offset = CalculateOffsetFromUop(offset);
        return offset;
    }

    /// <summary>
    /// Gets the byte offset of a static index entry within the staidx file.
    /// </summary>
    /// <param name="x">The block X coordinate.</param>
    /// <param name="y">The block Y coordinate.</param>
    /// <returns>The byte offset for the requested static index entry.</returns>
    public long GetStaidxOffset(ushort x, ushort y)
    {
        return GetBlockNumber(x, y) * 12;
    }

    /// <summary>
    /// Loads a block from the underlying land and statics files into the cache.
    /// </summary>
    /// <param name="x">The block X coordinate.</param>
    /// <param name="y">The block Y coordinate.</param>
    /// <returns>The loaded block.</returns>
    protected override Block LoadBlock(ushort x, ushort y)
    {
        AssertBlockCoords(x, y);
        _map.Position = GetMapOffset(x, y);
        var map = new LandBlock(this, x, y, _mapReader);

        _staidx.Position = GetStaidxOffset(x, y);
        var index = new GenericIndex(_staidxReader);
        var statics = new StaticBlock(this, x, y, _staticsReader, index);

        var block = new Block(map, statics);
        BlockCache.Add(block);
        return block;
    }

    /// <summary>
    /// Updates the radar-map color for the supplied block when its visible top tile changes.
    /// </summary>
    /// <param name="ns">The client session receiving live radar updates.</param>
    /// <param name="x">The tile X coordinate of the changed block origin.</param>
    /// <param name="y">The tile Y coordinate of the changed block origin.</param>
    public void UpdateRadar(NetState<CEDServer> ns, ushort x, ushort y)
    {
        if ((x & 0x7) != 0 || (y & 0x7) != 0)
            return;

        var landTile = GetLandTile(x, y);
        var landPriority = GetEffectiveAltitude(landTile);
        var radarId = landTile.Id;

        var block = GetStaticBlock((ushort)(x / 8), (ushort)(y / 8));
        block.SortTiles(ref TileDataProvider.StaticTiles);
        var topStaticTile = block.AllTiles().MaxBy(tile => tile.PriorityZ);

        // Radar tiles represent whichever surface is visually highest at the block origin,
        // which can be either the land tile or the top-most static tile.
        if (topStaticTile?.PriorityZ > landPriority)
            radarId = (ushort)(topStaticTile.Id + 0x4000);

        _radarMap.Update(ns, (ushort)(x / 8), (ushort)(y / 8), radarId);
    }

    /// <summary>
    /// Gets the raw altitude of a land tile.
    /// </summary>
    /// <param name="x">The tile X coordinate.</param>
    /// <param name="y">The tile Y coordinate.</param>
    /// <returns>The land-tile altitude.</returns>
    public sbyte GetLandAlt(ushort x, ushort y)
    {
        return GetLandTile(x, y).Z;
    }

    /// <summary>
    /// Estimates the effective visible altitude of a land tile based on its surrounding slope.
    /// </summary>
    /// <param name="tile">The land tile whose visual altitude should be estimated.</param>
    /// <returns>The effective altitude used for radar prioritization.</returns>
    public sbyte GetEffectiveAltitude(LandTile tile)
    {
        var north = tile.Z;
        var west = GetLandAlt(tile.X, (ushort)(tile.Y + 1));
        var south = GetLandAlt((ushort)(tile.X + 1), (ushort)(tile.Y + 1));
        var east = GetLandAlt((ushort)(tile.X + 1), tile.Y);

        if (Math.Abs(north - south) > Math.Abs(west - east))
        {
            return (sbyte)(north + south / 2);
        }
        else
        {
            return (sbyte)((west + east) / 2);
        }
    }

    /// <summary>
    /// Flushes cached blocks and the underlying land and statics streams.
    /// </summary>
    public void Flush()
    {
        BlockCache.Clear();
        _map.Flush();
        _staidx.Flush();
        _statics.Flush();
    }

    /// <summary>
    /// Writes backup copies of the map, staidx, and statics files into the supplied directory.
    /// </summary>
    /// <param name="backupDir">The directory that will receive the backup files.</param>
    public void Backup(string backupDir)
    {
        Directory.CreateDirectory(backupDir);
        foreach (var fs in new[] { _map, _staidx, _statics })
        {
            FileInfo fi = new FileInfo(fs.Name);
            Backup(fs, $"{backupDir}/{fi.Name}");
        }
    }

    /// <summary>
    /// Writes a full copy of one backing file to a backup path.
    /// </summary>
    /// <param name="file">The source file stream.</param>
    /// <param name="backupPath">The destination backup path.</param>
    private void Backup(FileStream file, String backupPath)
    {
        using var backupStream = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write);
        file.Position = 0;
        file.CopyTo(backupStream);
    }

    /// <summary>
    /// Persists a modified land block back into the map file.
    /// </summary>
    /// <param name="landBlock">The land block to save.</param>
    public void SaveBlock(LandBlock landBlock)
    {
        _logger.LogDebug($"Saving mapBlock {landBlock.X},{landBlock.Y}");
        _map.Position = GetMapOffset(landBlock.X, landBlock.Y);
        landBlock.Write(_mapWriter);
        landBlock.Changed = false;
    }

    /// <summary>
    /// Persists a modified static block back into the statics and staidx files.
    /// </summary>
    /// <param name="staticBlock">The static block to save.</param>
    public void SaveBlock(StaticBlock staticBlock)
    {
        _logger.LogDebug($"Saving staticBlock {staticBlock.X},{staticBlock.Y}");
        _staidx.Position = GetStaidxOffset(staticBlock.X, staticBlock.Y);
        var index = new GenericIndex(_staidxReader);
        var size = staticBlock.TotalSize;
        if (size > index.Length || index.Lookup <= 0)
        {
            _statics.Position = _statics.Length;
            index.Lookup = (int)_statics.Position;
        }

        index.Length = size;
        if (size == 0)
        {
            index.Lookup = -1;
        }
        else
        {
            _statics.Position = index.Lookup;
            staticBlock.Write(_staticsWriter);
        }

        // Staidx entries either point at the rewritten static payload or are reset to -1 when the block is empty.
        _staidx.Seek(-12, SeekOrigin.Current);
        index.Write(_staidxWriter);
        staticBlock.Changed = false;
    }

    private long MapFileBytes
    {
        get
        {
            if (IsUop)
                return UopFiles.Sum(f => f.Length);
            else
            {
                return _map.Length;
            }
        }
    }


    /// <summary>
    /// Validates that the configured map, staidx, and statics files match the expected dimensions and layout.
    /// </summary>
    private void Validate()
    {
        var mapBlocks = Width * Height;
        var mapBytes = mapBlocks * LandBlock.SIZE;
        var staidxBytes = mapBlocks * GenericIndex.Size;
        var mapFileBlocks = MapFileBytes / LandBlock.SIZE;
        var staidxFileBytes = _staidx.Length;
        var staidxFileBlocks = staidxFileBytes / GenericIndex.Size;

        var valid = true;
        if ((IsMul && MapFileBytes != mapBytes) || (IsUop && MapFileBytes < mapBytes))
        {
            _logger.LogError($"{_map.Name} file doesn't match configured size: {MapFileBytes} != {mapBytes}");
            _logger.LogInfo($"{_map.Name} seems to be {MapSizeHint()}");
            valid = false;
        }

        if (IsUop && MapFileBytes > mapBytes)
        {
            var diff = MapFileBytes - mapBytes;
            var blocksDiff = diff / LandBlock.SIZE;
            _logger.LogInfo($"{_map.Name} is larger than configured size by {blocksDiff} blocks ({diff} bytes)");
            if (blocksDiff == 1)
            {
                _logger.LogInfo("This is normal for newer clients.");
            }
            else
            {
                _logger.LogInfo("Either configuration is wrong or there is something wrong with the uop");
            }
        }

        if (staidxFileBytes != staidxBytes)
        {
            _logger.LogError($"{_staidx.Name} file doesn't match configured size: {_staidx.Length} != {staidxBytes}");
            _logger.LogInfo($"{_staidx.Name} seems to be {StaidxSizeHint()}");
            valid = false;
        }

        if ((IsMul && mapFileBlocks != staidxFileBlocks) || (IsUop && mapFileBlocks < staidxFileBlocks))
        {
            _logger.LogError
            (
                $"{_map.Name} file doesn't match {_staidx.Name} file in blocks: {mapFileBlocks} != {staidxFileBlocks} "
            );
            _logger.LogInfo($"{_map.Name} seems to be {MapSizeHint()}, and staidx seems to be {StaidxSizeHint()}");
            valid = false;
        }

        if (IsMul && MapFileBytes == mapBytes + LandBlock.SIZE)
        {
            _logger.LogError($"{_map.Name} file is exactly one block larger than configured size");
            _logger.LogInfo("If extracted from UOP, then client version is too new for this UOP extractor");
            var mapPath = _map.Name + ".extrablock";
            _logger.LogInfo($"Backing up map file to {mapPath}");
            Backup(_map, mapPath);
            _logger.LogInfo("Removing excessive map block");
            _map.SetLength(_map.Length - 196);
            Validate();
        }

        if (valid)
        {
            List<(ushort, ushort)> toFix = new();
            for (ushort x = 0; x < Width; x++)
            {
                for (ushort y = 0; y < Height; y++)
                {
                    _staidxReader.BaseStream.Seek(GetStaidxOffset(x, y), SeekOrigin.Begin);
                    var index = new GenericIndex(_staidxReader);
                    if (index.Lookup >= _statics.Length && index.Length > 0)
                    {
                        _logger.LogWarn($"Static block {x},{y} beyond file stream. Lookup: {index.Lookup}, Length: {index.Length}");
                        toFix.Add((x,y));
                    }
                }
            }
            if (toFix.Count > 0)
            {
                Console.WriteLine("Do you wish to drop these blocks to fix statics file? [y/n]");
                if (Console.ReadLine() == "y")
                {
                    foreach (var (x,y) in toFix)
                    {
                        var offset = GetStaidxOffset(x, y);
                        _staidx.Position = offset;
                        GenericIndex.Empty.Write(_staidxWriter);
                    }
                    _logger.LogInfo($"Fixed {toFix.Count} blocks.");
                }
            }
        }

        if (!valid)
        {
            throw new Exception("Invalid configuration");
        }
    }

    private string MapSizeHint()
    {
        return MapFileBytes switch
        {
            3_211_264 => "128x128 (map0 Pre-Alpha)",
            77_070_336 => "768x512 (map0,map1 Pre-ML)",
            89_915_392 => "896x512 (map0,map1 Post-ML)",
            11_289_600 => "288x200 (map2)",
            16_056_320 => "320x256 (map3) or 160x512(map5)",
            6_421_156 => "160x512 (map4)",
            _ => "Unknown size"
        };
    }

    /// <summary>
    /// Produces a human-readable hint for common staidx file sizes.
    /// </summary>
    /// <returns>A description of the closest known staidx size.</returns>
    private string StaidxSizeHint()
    {
        return _staidx.Length switch
        {
            196_608 => "128x128 (map0 Pre-Alpha)",
            4_718_592 => "768x512 (map0,map1 Pre-ML)",
            5_505_024 => "896x512 (map0,map1 Post-ML)",
            691_200 => "288x200 (map2)",
            983_040 => "320x256 (map3) or 160x512(map5)",
            393_132 => "160x512 (map4)",
            _ => "Unknown size"
        };
    }

    /// <summary>
    /// Reads the UOP file table so block offsets can be mapped into the container entries.
    /// </summary>
    /// <param name="pattern">The build-path naming pattern used inside the UOP container.</param>
    private void ReadUopFiles(string pattern)
    {
        _map.Seek(0, SeekOrigin.Begin);

        if (_mapReader.ReadInt32() != 0x50594D)
        {
            throw new ArgumentException("Bad UOP file.");
        }

        _mapReader.ReadInt64(); // version + signature
        long nextBlock = _mapReader.ReadInt64();
        _mapReader.ReadInt32(); // block capacity
        int count = _mapReader.ReadInt32();

        UopFiles = new UopFile[count];

        var hashes = new Dictionary<ulong, int>();

        for (int i = 0; i < count; i++)
        {
            string file = $"build/{pattern}/{i:D8}.dat";
            ulong hash = Uop.HashFileName(file);

            hashes.TryAdd(hash, i);
        }

        _map.Seek(nextBlock, SeekOrigin.Begin);

        do
        {
            int filesCount = _mapReader.ReadInt32();
            nextBlock = _mapReader.ReadInt64();

            for (int i = 0; i < filesCount; i++)
            {
                long offset = _mapReader.ReadInt64();
                int headerLength = _mapReader.ReadInt32();
                int compressedLength = _mapReader.ReadInt32();
                int decompressedLength = _mapReader.ReadInt32();
                ulong hash = _mapReader.ReadUInt64();
                _mapReader.ReadUInt32(); // Adler32
                short flag = _mapReader.ReadInt16();

                int length = flag == 1 ? compressedLength : decompressedLength;

                if (offset == 0)
                {
                    continue;
                }

                if (hashes.TryGetValue(hash, out int idx))
                {
                    if (idx < 0 || idx > UopFiles.Length)
                    {
                        throw new IndexOutOfRangeException
                            ("hashes dictionary and files collection have different count of entries!");
                    }

                    UopFiles[idx] = new UopFile(offset + headerLength, length);
                }
                else
                {
                    throw new ArgumentException
                    (
                        $"File with hash 0x{hash:X8} was not found in hashes dictionary! EA Mythic changed UOP format!"
                    );
                }
            }
        } while (_map.Seek(nextBlock, SeekOrigin.Begin) != 0);
    }

    private long CalculateOffsetFromUop(long offset)
    {
        long pos = 0;

        foreach (UopFile t in UopFiles)
        {
            var currentPosition = pos + t.Length;

            if (offset < currentPosition)
            {
                return t.Offset + (offset - pos);
            }

            pos = currentPosition;
        }

        return _map.Length;
    }

    //Deduplicate and defrag statics
    internal void SuperSave()
    {
        using (var staidxBak = File.OpenWrite("staidxbak.mul"))
        {
            _staidx.Seek(0, SeekOrigin.Begin);
            _staidx.CopyTo(staidxBak);
            _staidx.Seek(0, SeekOrigin.Begin);
        }
        using (var staticsBak = File.OpenWrite("staticsbak.mul"))
        {
            _statics.Seek(0, SeekOrigin.Begin);
            _statics.CopyTo(staticsBak);
            _statics.Seek(0, SeekOrigin.Begin);
        }

        using var staidxRead = File.OpenRead("staidxbak.mul");
        using var staidxReader = new  BinaryReader(staidxRead);
        using var staticsRead = File.OpenRead("staticsbak.mul");
        using var staticsReader = new  BinaryReader(staticsRead);
        _statics.SetLength(0); //Empty statics file

        for (ushort x = 0; x < Width; x++)
        {
            for (ushort y = 0; y < Height; y++)
            {
                staidxRead.Position = GetStaidxOffset(x, y);
                var index = new GenericIndex(staidxReader);
                var block = new StaticBlock(this, x, y, staticsReader, index);
                block.Deduplicate();
                var newIndex = new GenericIndex((int)_statics.Position, block.TotalSize, 0);
                block.Write(_staticsWriter);
                newIndex.Write(_staidxWriter);
            }
        }
        _staidxWriter.Flush();
        _staticsWriter.Flush();
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            _map.Dispose();
            _statics.Dispose();
            _staidx.Dispose();
            _mapReader.Dispose();
            _staticsReader.Dispose();
            _staidxReader.Dispose();
            _mapWriter.Dispose();
            _staticsWriter.Dispose();
            _staidxWriter.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public override void LogInfo(string message)
    {
        _logger.LogInfo(message);
    }

    public override void LogWarn(string message)
    {
        _logger.LogWarn(message);
    }

    public override void LogError(string message)
    {
        _logger.LogError(message);
    }

    public override void LogDebug(string message)
    {
        _logger.LogDebug(message);
    }
}