using ClassicUO.Assets;
using System.Text;

namespace CentrED.Assets;

/// <summary>
/// Immutable snapshot of one hue entry loaded from hues.mul.
/// </summary>
public readonly record struct AssetHueEntry(
    ushort HueId,
    string Name,
    ushort TableStart,
    ushort TableEnd,
    ushort[] Colors);

/// <summary>
/// Immutable snapshot of one land tile-data entry loaded from tiledata.mul.
/// </summary>
public readonly record struct AssetLandTileDataEntry(
    ushort TileId,
    string Name,
    ushort TextureId,
    TileFlag Flags);

/// <summary>
/// Immutable snapshot of one item tile-data entry loaded from tiledata.mul.
/// </summary>
public readonly record struct AssetItemTileDataEntry(
    ushort TileId,
    string Name,
    short Animation,
    byte Weight,
    byte Quality,
    byte Quantity,
    byte Hue,
    byte StackingOffset,
    byte Value,
    byte Height,
    short MiscData,
    byte Unknown2,
    byte Unknown3,
    TileFlag Flags);

/// <summary>
/// Loads, edits, imports, exports, and saves hues.mul and tiledata.mul directly from a local
/// Ultima client directory for the asset workspace.
/// </summary>
public sealed class AssetHueTileDataService
{
    private sealed class HueEntryData
    {
        public string Name { get; set; } = string.Empty;
        public ushort TableStart { get; set; }
        public ushort TableEnd { get; set; }
        public ushort[] Colors { get; set; } = new ushort[32];

        public HueEntryData Clone()
        {
            return new HueEntryData
            {
                Name = Name,
                TableStart = TableStart,
                TableEnd = TableEnd,
                Colors = (ushort[])Colors.Clone(),
            };
        }
    }

    private struct LandEntryData
    {
        public string Name;
        public ushort TextureId;
        public TileFlag Flags;
    }

    private struct ItemEntryData
    {
        public string Name;
        public short Animation;
        public byte Weight;
        public byte Quality;
        public byte Quantity;
        public byte Hue;
        public byte StackingOffset;
        public byte Value;
        public byte Height;
        public short MiscData;
        public byte Unknown2;
        public byte Unknown3;
        public TileFlag Flags;
    }

    private static readonly Encoding Encoding1252 = Encoding.GetEncoding(1252);

    private readonly List<ushort> _hueIds = [];
    private readonly List<ushort> _landIds = [];
    private readonly List<ushort> _itemIds = [];
    private readonly HashSet<ushort> _dirtyHueIds = [];
    private readonly HashSet<ushort> _dirtyLandIds = [];
    private readonly HashSet<ushort> _dirtyItemIds = [];
    private List<TileFlag> _supportedFlags = [];

    private int[] _hueHeaders = [];
    private int[] _landHeaders = [];
    private int[] _itemHeaders = [];
    private HueEntryData[] _hues = [];
    private HueEntryData[] _originalHues = [];
    private LandEntryData[] _lands = [];
    private LandEntryData[] _originalLands = [];
    private ItemEntryData[] _items = [];
    private ItemEntryData[] _originalItems = [];

    /// <summary>
    /// Gets the root path currently loaded into the browser service.
    /// </summary>
    public string LoadedRootPath { get; private set; } = string.Empty;

    /// <summary>
    /// Gets whether the local hue and tiledata files are currently loaded and ready.
    /// </summary>
    public bool IsReady { get; private set; }

    /// <summary>
    /// Gets the last service status message.
    /// </summary>
    public string StatusMessage { get; private set; } = "Choose a Ultima data directory to start browsing hues and tiledata.";

    /// <summary>
    /// Gets the number of staged hue and tiledata changes that have not yet been persisted.
    /// </summary>
    public int DirtyCount => _dirtyHueIds.Count + _dirtyLandIds.Count + _dirtyItemIds.Count;

    /// <summary>
    /// Gets the ids of all loaded hue entries.
    /// </summary>
    public IReadOnlyList<ushort> HueIds => _hueIds;

    /// <summary>
    /// Gets the ids of all loaded land tile-data entries.
    /// </summary>
    public IReadOnlyList<ushort> LandIds => _landIds;

    /// <summary>
    /// Gets the ids of all loaded item tile-data entries.
    /// </summary>
    public IReadOnlyList<ushort> ItemIds => _itemIds;

    /// <summary>
    /// Gets the tile flags valid for the loaded tiledata format.
    /// </summary>
    public IReadOnlyList<TileFlag> SupportedFlags => _supportedFlags;

    /// <summary>
    /// Gets whether the loaded tiledata uses the newer 64-bit flag format.
    /// </summary>
    public bool UsesExtendedTileDataFormat { get; private set; }

    /// <summary>
    /// Loads hues.mul and tiledata.mul from the provided root path.
    /// </summary>
    public void EnsureLoaded(string rootPath)
    {
        if (IsReady && string.Equals(LoadedRootPath, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ResetState();

        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            StatusMessage = "The selected Ultima data directory does not exist.";
            return;
        }

        var huesPath = Path.Combine(rootPath, "hues.mul");
        var tiledataPath = Path.Combine(rootPath, "tiledata.mul");
        if (!File.Exists(huesPath) || !File.Exists(tiledataPath))
        {
            StatusMessage = "Both hues.mul and tiledata.mul are required before this family can be edited.";
            return;
        }

        try
        {
            LoadHues(huesPath);
            LoadTileData(tiledataPath);

            LoadedRootPath = AssetWorkspaceService.NormalizePath(rootPath);
            IsReady = true;
            StatusMessage = $"Loaded {_hues.Length} hues, {_lands.Length} land entries, and {_items.Length} item entries from {LoadedRootPath}.";
        }
        catch (Exception ex)
        {
            ResetState();
            StatusMessage = $"Failed to load hues and tiledata: {ex.Message}";
        }
    }

    /// <summary>
    /// Filters hue ids using name and numeric text matching.
    /// </summary>
    public List<ushort> GetFilteredHueIds(string filterText)
    {
        var results = new List<ushort>();
        if (!IsReady)
        {
            return results;
        }

        if (string.IsNullOrWhiteSpace(filterText))
        {
            results.AddRange(_hueIds);
            return results;
        }

        foreach (var id in _hueIds)
        {
            var hue = _hues[id];
            if (MatchesIdOrName(id, hue.Name, filterText))
            {
                results.Add(id);
            }
        }

        return results;
    }

    /// <summary>
    /// Filters land or item tile-data ids using text and tile-flag matching.
    /// </summary>
    public List<ushort> GetFilteredTileIds(bool landMode, string filterText, ulong flagMask, bool inclusive, bool matchAll)
    {
        var results = new List<ushort>();
        if (!IsReady)
        {
            return results;
        }

        var ids = landMode ? _landIds : _itemIds;
        foreach (var id in ids)
        {
            if (landMode)
            {
                var entry = _lands[id];
                if (!MatchesIdOrName(id, entry.Name, filterText) || !MatchesFlags(entry.Flags, flagMask, inclusive, matchAll))
                {
                    continue;
                }
            }
            else
            {
                var entry = _items[id];
                if (!MatchesIdOrName(id, entry.Name, filterText) || !MatchesFlags(entry.Flags, flagMask, inclusive, matchAll))
                {
                    continue;
                }
            }

            results.Add(id);
        }

        return results;
    }

    /// <summary>
    /// Returns one hue entry snapshot.
    /// </summary>
    public AssetHueEntry GetHueEntry(ushort hueId)
    {
        EnsureValidHueId(hueId);
        var entry = _hues[hueId];
        return new AssetHueEntry(hueId, entry.Name, entry.TableStart, entry.TableEnd, (ushort[])entry.Colors.Clone());
    }

    /// <summary>
    /// Returns one land tile-data entry snapshot.
    /// </summary>
    public AssetLandTileDataEntry GetLandEntry(ushort tileId)
    {
        EnsureValidLandId(tileId);
        var entry = _lands[tileId];
        return new AssetLandTileDataEntry(tileId, entry.Name, entry.TextureId, entry.Flags);
    }

    /// <summary>
    /// Returns one item tile-data entry snapshot.
    /// </summary>
    public AssetItemTileDataEntry GetItemEntry(ushort tileId)
    {
        EnsureValidItemId(tileId);
        var entry = _items[tileId];
        return new AssetItemTileDataEntry(tileId, entry.Name, entry.Animation, entry.Weight, entry.Quality, entry.Quantity, entry.Hue, entry.StackingOffset, entry.Value, entry.Height, entry.MiscData, entry.Unknown2, entry.Unknown3, entry.Flags);
    }

    /// <summary>
    /// Applies edits to one hue entry.
    /// </summary>
    public void UpdateHueEntry(ushort hueId, string name, ushort tableStart, ushort tableEnd, ushort[] colors)
    {
        EnsureValidHueId(hueId);
        if (colors.Length != 32)
        {
            throw new InvalidOperationException("Hues require exactly 32 colors.");
        }

        _hues[hueId] = new HueEntryData
        {
            Name = NormalizeName(name),
            TableStart = tableStart,
            TableEnd = tableEnd,
            Colors = (ushort[])colors.Clone(),
        };

        UpdateHueDirtyState(hueId);
    }

    /// <summary>
    /// Restores one hue entry from the original loaded file state.
    /// </summary>
    public void RevertHueEntry(ushort hueId)
    {
        EnsureValidHueId(hueId);
        _hues[hueId] = _originalHues[hueId].Clone();
        _dirtyHueIds.Remove(hueId);
    }

    /// <summary>
    /// Applies edits to one land tile-data entry.
    /// </summary>
    public void UpdateLandEntry(ushort tileId, string name, ushort textureId, TileFlag flags)
    {
        EnsureValidLandId(tileId);
        _lands[tileId] = new LandEntryData
        {
            Name = NormalizeName(name),
            TextureId = textureId,
            Flags = SanitizeFlags(flags),
        };

        UpdateLandDirtyState(tileId);
    }

    /// <summary>
    /// Restores one land tile-data entry from the original loaded file state.
    /// </summary>
    public void RevertLandEntry(ushort tileId)
    {
        EnsureValidLandId(tileId);
        _lands[tileId] = _originalLands[tileId];
        _dirtyLandIds.Remove(tileId);
    }

    /// <summary>
    /// Applies edits to one item tile-data entry.
    /// </summary>
    public void UpdateItemEntry(ushort tileId, AssetItemTileDataEntry entry)
    {
        EnsureValidItemId(tileId);
        _items[tileId] = new ItemEntryData
        {
            Name = NormalizeName(entry.Name),
            Animation = entry.Animation,
            Weight = entry.Weight,
            Quality = entry.Quality,
            Quantity = entry.Quantity,
            Hue = entry.Hue,
            StackingOffset = entry.StackingOffset,
            Value = entry.Value,
            Height = entry.Height,
            MiscData = entry.MiscData,
            Unknown2 = entry.Unknown2,
            Unknown3 = entry.Unknown3,
            Flags = SanitizeFlags(entry.Flags),
        };

        UpdateItemDirtyState(tileId);
    }

    /// <summary>
    /// Restores one item tile-data entry from the original loaded file state.
    /// </summary>
    public void RevertItemEntry(ushort tileId)
    {
        EnsureValidItemId(tileId);
        _items[tileId] = _originalItems[tileId];
        _dirtyItemIds.Remove(tileId);
    }

    /// <summary>
    /// Gets whether one hue entry currently has staged edits.
    /// </summary>
    public bool IsHueDirty(ushort hueId) => _dirtyHueIds.Contains(hueId);

    /// <summary>
    /// Gets whether one land entry currently has staged edits.
    /// </summary>
    public bool IsLandDirty(ushort tileId) => _dirtyLandIds.Contains(tileId);

    /// <summary>
    /// Gets whether one item entry currently has staged edits.
    /// </summary>
    public bool IsItemDirty(ushort tileId) => _dirtyItemIds.Contains(tileId);

    /// <summary>
    /// Exports one hue to the same text format used by UO Fiddler.
    /// </summary>
    public void ExportHue(ushort hueId, string outputPath)
    {
        var entry = GetHueEntry(hueId);
        using var writer = new StreamWriter(new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite), Encoding1252);
        writer.WriteLine(entry.Name);
        writer.WriteLine(entry.TableStart);
        writer.WriteLine(entry.TableEnd);
        foreach (var color in entry.Colors)
        {
            writer.WriteLine(color);
        }
    }

    /// <summary>
    /// Imports one hue from the same text format used by UO Fiddler.
    /// </summary>
    public void ImportHue(ushort hueId, string inputPath)
    {
        EnsureValidHueId(hueId);
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("The selected hue import file was not found.", inputPath);
        }

        using var reader = new StreamReader(inputPath);
        var imported = _hues[hueId].Clone();
        var colorIndex = -3;

        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (colorIndex >= imported.Colors.Length)
            {
                break;
            }

            switch (colorIndex)
            {
                case -3:
                    imported.Name = NormalizeName(line);
                    break;
                case -2:
                    imported.TableStart = ushort.Parse(line);
                    break;
                case -1:
                    imported.TableEnd = ushort.Parse(line);
                    break;
                default:
                    imported.Colors[colorIndex] = ushort.Parse(line);
                    break;
            }

            colorIndex++;
        }

        _hues[hueId] = imported;
        UpdateHueDirtyState(hueId);
    }

    /// <summary>
    /// Exports the full hue name list in the same format used by UO Fiddler.
    /// </summary>
    public void ExportHueList(string outputPath)
    {
        var builder = new StringBuilder(_hues.Length * 24);
        for (var i = 0; i < _hues.Length; i++)
        {
            builder.Append("0x").AppendFormat("{0:X}", i).Append(' ').AppendLine(_hues[i].Name);
        }

        File.WriteAllText(outputPath, builder.ToString());
    }

    /// <summary>
    /// Saves all current hue edits back into hues.mul and refreshes the original snapshot.
    /// </summary>
    public void SaveHues()
    {
        EnsureReady();
        var huesPath = Path.Combine(LoadedRootPath, "hues.mul");
        var backupPath = huesPath + ".bak";
        var tempPath = huesPath + ".tmp";

        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }

        File.Copy(huesPath, backupPath, true);

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                var index = 0;
                foreach (var header in _hueHeaders)
                {
                    writer.Write(header);
                    for (var j = 0; j < 8 && index < _hues.Length; j++, index++)
                    {
                        var entry = _hues[index];
                        foreach (var color in entry.Colors)
                        {
                            writer.Write(color);
                        }

                        writer.Write(entry.TableStart);
                        writer.Write(entry.TableEnd);
                        writer.Write(ToFixedAscii(entry.Name, 20));
                    }
                }
            }

            File.Copy(tempPath, huesPath, true);
            File.Delete(tempPath);
            RefreshOriginalHueState();
            _dirtyHueIds.Clear();
            StatusMessage = $"Saved {_hues.Length} hues to {huesPath}.";
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    /// <summary>
    /// Saves all current tiledata edits back into tiledata.mul and refreshes the original snapshot.
    /// </summary>
    public void SaveTileData()
    {
        EnsureReady();
        var tiledataPath = Path.Combine(LoadedRootPath, "tiledata.mul");
        var backupPath = tiledataPath + ".bak";
        var tempPath = tiledataPath + ".tmp";

        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }

        File.Copy(tiledataPath, backupPath, true);

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                var headerIndex = 0;
                for (var i = 0; i < _lands.Length; i++)
                {
                    if ((i & 0x1F) == 0)
                    {
                        writer.Write(_landHeaders[headerIndex++]);
                    }

                    var entry = _lands[i];
                    WriteTileFlags(writer, entry.Flags);
                    writer.Write(entry.TextureId);
                    writer.Write(ToFixedAscii(entry.Name, 20));
                }

                headerIndex = 0;
                for (var i = 0; i < _items.Length; i++)
                {
                    if ((i & 0x1F) == 0)
                    {
                        writer.Write(_itemHeaders[headerIndex++]);
                    }

                    var entry = _items[i];
                    WriteTileFlags(writer, entry.Flags);
                    writer.Write(entry.Weight);
                    writer.Write(entry.Quality);
                    writer.Write(entry.MiscData);
                    writer.Write(entry.Unknown2);
                    writer.Write(entry.Quantity);
                    writer.Write(entry.Animation);
                    writer.Write(entry.Unknown3);
                    writer.Write(entry.Hue);
                    writer.Write(entry.StackingOffset);
                    writer.Write(entry.Value);
                    writer.Write(entry.Height);
                    writer.Write(ToFixedAscii(entry.Name, 20));
                }
            }

            File.Copy(tempPath, tiledataPath, true);
            File.Delete(tempPath);
            RefreshOriginalTileDataState();
            _dirtyLandIds.Clear();
            _dirtyItemIds.Clear();
            StatusMessage = $"Saved tiledata to {tiledataPath}.";
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    /// <summary>
    /// Exports the current land table to UO Fiddler-compatible CSV.
    /// </summary>
    public void ExportLandDataToCsv(string outputPath)
    {
        using var writer = new StreamWriter(new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite));
        writer.Write("ID;Name;TextureId");
        writer.Write(GetTileFlagColumnNames());
        writer.Write("\r\n");

        foreach (var id in _landIds)
        {
            var entry = _lands[id];
            writer.Write("0x{0:X4}", id);
            writer.Write($";{entry.Name}");
            writer.Write($";0x{entry.TextureId:X4}");
            WriteFlagColumns(writer, entry.Flags);
            writer.Write("\r\n");
        }
    }

    /// <summary>
    /// Exports the current item table to UO Fiddler-compatible CSV.
    /// </summary>
    public void ExportItemDataToCsv(string outputPath)
    {
        using var writer = new StreamWriter(new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite), Encoding1252);
        writer.Write("ID;Name;Weight/Quantity;Layer/Quality;Gump/AnimID;Height;Hue;Class/Quantity;StackingOffset;miscData;Unknown2;Unknown3");
        writer.Write(GetTileFlagColumnNames());
        writer.Write("\r\n");

        foreach (var id in _itemIds)
        {
            var entry = _items[id];
            writer.Write("0x{0:X4}", id);
            writer.Write($";{entry.Name}");
            writer.Write($";{entry.Weight}");
            writer.Write($";{entry.Quality}");
            writer.Write(";0x{0:X4}", entry.Animation);
            writer.Write($";{entry.Height}");
            writer.Write($";{entry.Hue}");
            writer.Write($";{entry.Quantity}");
            writer.Write($";{entry.StackingOffset}");
            writer.Write($";{entry.MiscData}");
            writer.Write($";{entry.Unknown2}");
            writer.Write($";{entry.Unknown3}");
            WriteFlagColumns(writer, entry.Flags);
            writer.Write("\r\n");
        }
    }

    /// <summary>
    /// Imports land CSV data in UO Fiddler-compatible format.
    /// </summary>
    public void ImportLandDataFromCsv(string inputPath)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("The selected land CSV import file was not found.", inputPath);
        }

        using var reader = new StreamReader(inputPath);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("ID;", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var split = line.Split(';');
            if (split.Length < 3 + _supportedFlags.Count)
            {
                continue;
            }

            var id = checked((ushort)ConvertStringToInt(split[0]));
            EnsureValidLandId(id);

            var flags = ReadFlagsFromColumns(split, 3);
            _lands[id] = new LandEntryData
            {
                Name = NormalizeName(split[1]),
                TextureId = checked((ushort)ConvertStringToInt(split[2])),
                Flags = flags,
            };

            UpdateLandDirtyState(id);
        }
    }

    /// <summary>
    /// Imports item CSV data in UO Fiddler-compatible format.
    /// </summary>
    public void ImportItemDataFromCsv(string inputPath)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("The selected item CSV import file was not found.", inputPath);
        }

        using var reader = new StreamReader(inputPath);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("ID;", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var split = line.Split(';');
            if (split.Length < 12 + _supportedFlags.Count)
            {
                continue;
            }

            var id = checked((ushort)ConvertStringToInt(split[0]));
            EnsureValidItemId(id);

            _items[id] = new ItemEntryData
            {
                Name = NormalizeName(split[1]),
                Weight = checked((byte)ConvertStringToInt(split[2])),
                Quality = checked((byte)ConvertStringToInt(split[3])),
                Animation = checked((short)ConvertStringToInt(split[4])),
                Height = checked((byte)ConvertStringToInt(split[5])),
                Hue = checked((byte)ConvertStringToInt(split[6])),
                Quantity = checked((byte)ConvertStringToInt(split[7])),
                StackingOffset = checked((byte)ConvertStringToInt(split[8])),
                MiscData = checked((short)ConvertStringToInt(split[9])),
                Unknown2 = checked((byte)ConvertStringToInt(split[10])),
                Unknown3 = checked((byte)ConvertStringToInt(split[11])),
                Flags = ReadFlagsFromColumns(split, 12),
            };

            UpdateItemDirtyState(id);
        }
    }

    private void LoadHues(string huesPath)
    {
        using var stream = new FileStream(huesPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);
        var blockCount = Math.Min((int)(stream.Length / 708), 375);

        _hueHeaders = new int[blockCount];
        _hues = new HueEntryData[blockCount * 8];
        _hueIds.Clear();

        var index = 0;
        for (var i = 0; i < blockCount; i++)
        {
            _hueHeaders[i] = reader.ReadInt32();
            for (var j = 0; j < 8; j++, index++)
            {
                var colors = new ushort[32];
                for (var colorIndex = 0; colorIndex < colors.Length; colorIndex++)
                {
                    colors[colorIndex] = reader.ReadUInt16();
                }

                _hues[index] = new HueEntryData
                {
                    TableStart = reader.ReadUInt16(),
                    TableEnd = reader.ReadUInt16(),
                    Name = ReadFixedString(reader, 20),
                    Colors = colors,
                };

                _hueIds.Add((ushort)index);
            }
        }

        RefreshOriginalHueState();
    }

    private void LoadTileData(string tiledataPath)
    {
        UsesExtendedTileDataFormat = new FileInfo(tiledataPath).Length >= 1644544;
        _supportedFlags = Enum.GetValues<TileFlag>()
            .Where(flag => flag != TileFlag.None)
            .Where(flag => UsesExtendedTileDataFormat || (ulong)flag <= uint.MaxValue)
            .ToList();

        using var stream = new FileStream(tiledataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);

        _landHeaders = new int[512];
        _lands = new LandEntryData[0x4000];
        _landIds.Clear();

        for (var i = 0; i < _lands.Length; i += 32)
        {
            _landHeaders[i / 32] = reader.ReadInt32();
            for (var count = 0; count < 32; count++)
            {
                var index = i + count;
                _lands[index] = new LandEntryData
                {
                    Flags = ReadTileFlags(reader),
                    TextureId = reader.ReadUInt16(),
                    Name = ReadFixedString(reader, 20),
                };
                _landIds.Add((ushort)index);
            }
        }

        var itemStructSize = UsesExtendedTileDataFormat ? 37 : 33;
        var remainingBytes = stream.Length - stream.Position;
        var itemHeaderCount = (int)(remainingBytes / ((itemStructSize * 32) + 4));

        _itemHeaders = new int[itemHeaderCount];
        _items = new ItemEntryData[itemHeaderCount * 32];
        _itemIds.Clear();

        for (var i = 0; i < _items.Length; i += 32)
        {
            _itemHeaders[i / 32] = reader.ReadInt32();
            for (var count = 0; count < 32; count++)
            {
                var index = i + count;
                _items[index] = new ItemEntryData
                {
                    Flags = ReadTileFlags(reader),
                    Weight = reader.ReadByte(),
                    Quality = reader.ReadByte(),
                    MiscData = reader.ReadInt16(),
                    Unknown2 = reader.ReadByte(),
                    Quantity = reader.ReadByte(),
                    Animation = reader.ReadInt16(),
                    Unknown3 = reader.ReadByte(),
                    Hue = reader.ReadByte(),
                    StackingOffset = reader.ReadByte(),
                    Value = reader.ReadByte(),
                    Height = reader.ReadByte(),
                    Name = ReadFixedString(reader, 20),
                };
                _itemIds.Add((ushort)index);
            }
        }

        RefreshOriginalTileDataState();
    }

    private void RefreshOriginalHueState()
    {
        _originalHues = new HueEntryData[_hues.Length];
        for (var i = 0; i < _hues.Length; i++)
        {
            _originalHues[i] = _hues[i].Clone();
        }
    }

    private void RefreshOriginalTileDataState()
    {
        _originalLands = new LandEntryData[_lands.Length];
        _lands.CopyTo(_originalLands, 0);

        _originalItems = new ItemEntryData[_items.Length];
        _items.CopyTo(_originalItems, 0);
    }

    private TileFlag ReadTileFlags(BinaryReader reader)
    {
        return UsesExtendedTileDataFormat
            ? (TileFlag)reader.ReadUInt64()
            : (TileFlag)reader.ReadUInt32();
    }

    private void WriteTileFlags(BinaryWriter writer, TileFlag flags)
    {
        if (UsesExtendedTileDataFormat)
        {
            writer.Write((ulong)flags);
            return;
        }

        writer.Write((uint)flags);
    }

    private string GetTileFlagColumnNames()
    {
        var builder = new StringBuilder(_supportedFlags.Count * 12);
        foreach (var flag in _supportedFlags)
        {
            builder.Append(';').Append(flag);
        }

        return builder.ToString();
    }

    private void WriteFlagColumns(StreamWriter writer, TileFlag flags)
    {
        foreach (var flag in _supportedFlags)
        {
            writer.Write($";{((flags & flag) != 0 ? "1" : "0")}");
        }
    }

    private TileFlag ReadFlagsFromColumns(string[] split, int startIndex)
    {
        var flags = TileFlag.None;
        for (var i = 0; i < _supportedFlags.Count && (startIndex + i) < split.Length; i++)
        {
            if (ConvertStringToInt(split[startIndex + i]) != 0)
            {
                flags |= _supportedFlags[i];
            }
        }

        return SanitizeFlags(flags);
    }

    private static string NormalizeName(string name)
    {
        return (name ?? string.Empty).Replace("\n", " ").Replace("\r", " ");
    }

    private static string ReadFixedString(BinaryReader reader, int length)
    {
        var bytes = reader.ReadBytes(length);
        var count = Array.IndexOf(bytes, (byte)0);
        if (count < 0)
        {
            count = bytes.Length;
        }

        return Encoding.ASCII.GetString(bytes, 0, count).Replace("\n", " ");
    }

    private static byte[] ToFixedAscii(string text, int length)
    {
        var buffer = new byte[length];
        var bytes = Encoding.ASCII.GetBytes(NormalizeName(text));
        if (bytes.Length > length)
        {
            Array.Resize(ref bytes, length);
        }

        bytes.CopyTo(buffer, 0);
        return buffer;
    }

    private TileFlag SanitizeFlags(TileFlag flags)
    {
        var mask = 0ul;
        foreach (var flag in _supportedFlags)
        {
            mask |= (ulong)flag;
        }

        return (TileFlag)((ulong)flags & mask);
    }

    private void UpdateHueDirtyState(ushort hueId)
    {
        if (AreEqual(_hues[hueId], _originalHues[hueId]))
        {
            _dirtyHueIds.Remove(hueId);
        }
        else
        {
            _dirtyHueIds.Add(hueId);
        }
    }

    private void UpdateLandDirtyState(ushort tileId)
    {
        if (AreEqual(_lands[tileId], _originalLands[tileId]))
        {
            _dirtyLandIds.Remove(tileId);
        }
        else
        {
            _dirtyLandIds.Add(tileId);
        }
    }

    private void UpdateItemDirtyState(ushort tileId)
    {
        if (AreEqual(_items[tileId], _originalItems[tileId]))
        {
            _dirtyItemIds.Remove(tileId);
        }
        else
        {
            _dirtyItemIds.Add(tileId);
        }
    }

    private static bool AreEqual(HueEntryData left, HueEntryData right)
    {
        if (left.Name != right.Name || left.TableStart != right.TableStart || left.TableEnd != right.TableEnd)
        {
            return false;
        }

        return left.Colors.AsSpan().SequenceEqual(right.Colors);
    }

    private static bool AreEqual(LandEntryData left, LandEntryData right)
    {
        return left.Name == right.Name && left.TextureId == right.TextureId && left.Flags == right.Flags;
    }

    private static bool AreEqual(ItemEntryData left, ItemEntryData right)
    {
        return left.Name == right.Name &&
               left.Animation == right.Animation &&
               left.Weight == right.Weight &&
               left.Quality == right.Quality &&
               left.Quantity == right.Quantity &&
               left.Hue == right.Hue &&
               left.StackingOffset == right.StackingOffset &&
               left.Value == right.Value &&
               left.Height == right.Height &&
               left.MiscData == right.MiscData &&
               left.Unknown2 == right.Unknown2 &&
               left.Unknown3 == right.Unknown3 &&
               left.Flags == right.Flags;
    }

    private static bool MatchesIdOrName(ushort id, string name, string filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText))
        {
            return true;
        }

        return id.ToString().Contains(filterText, StringComparison.InvariantCultureIgnoreCase) ||
               $"0x{id:X4}".Contains(filterText, StringComparison.InvariantCultureIgnoreCase) ||
               name.Contains(filterText, StringComparison.InvariantCultureIgnoreCase);
    }

    private static bool MatchesFlags(TileFlag flags, ulong flagMask, bool inclusive, bool matchAll)
    {
        if (flagMask == 0)
        {
            return true;
        }

        var value = (ulong)flags;
        var matched = matchAll
            ? (value & flagMask) == flagMask
            : (value & flagMask) > 0;

        return inclusive ? matched : !matched;
    }

    private static int ConvertStringToInt(string text)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToInt32(text[2..], 16);
        }

        return int.Parse(text);
    }

    private void EnsureReady()
    {
        if (!IsReady)
        {
            throw new InvalidOperationException("The hue and tiledata service is not ready.");
        }
    }

    private void EnsureValidHueId(ushort hueId)
    {
        EnsureReady();
        if (hueId >= _hues.Length)
        {
            throw new InvalidOperationException("The selected hue id is out of range.");
        }
    }

    private void EnsureValidLandId(ushort tileId)
    {
        EnsureReady();
        if (tileId >= _lands.Length)
        {
            throw new InvalidOperationException("The selected land tile id is out of range.");
        }
    }

    private void EnsureValidItemId(ushort tileId)
    {
        EnsureReady();
        if (tileId >= _items.Length)
        {
            throw new InvalidOperationException("The selected item tile id is out of range.");
        }
    }

    private void ResetState()
    {
        LoadedRootPath = string.Empty;
        IsReady = false;
        UsesExtendedTileDataFormat = false;

        _hueHeaders = [];
        _landHeaders = [];
        _itemHeaders = [];
        _hues = [];
        _originalHues = [];
        _lands = [];
        _originalLands = [];
        _items = [];
        _originalItems = [];
        _supportedFlags = [];

        _hueIds.Clear();
        _landIds.Clear();
        _itemIds.Clear();
        _dirtyHueIds.Clear();
        _dirtyLandIds.Clear();
        _dirtyItemIds.Clear();
    }
}