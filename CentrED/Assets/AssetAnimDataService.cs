using System.Text.Json;

namespace CentrED.Assets;

/// <summary>
/// Immutable snapshot of one animdata entry loaded from animdata.mul.
/// </summary>
public readonly record struct AssetAnimDataEntry(
    ushort BaseId,
    byte Unknown,
    byte FrameInterval,
    byte FrameStart,
    IReadOnlyList<sbyte> FrameOffsets);

/// <summary>
/// Loads, edits, imports, exports, and saves animdata.mul directly from a local Ultima client directory.
/// </summary>
public sealed class AssetAnimDataService
{
    private sealed class EntryData
    {
        public byte Unknown { get; set; }
        public byte FrameInterval { get; set; }
        public byte FrameStart { get; set; }
        public List<sbyte> FrameOffsets { get; set; } = [];

        public EntryData Clone()
        {
            return new EntryData
            {
                Unknown = Unknown,
                FrameInterval = FrameInterval,
                FrameStart = FrameStart,
                FrameOffsets = [.. FrameOffsets],
            };
        }
    }

    private sealed class ExportedAnimDataFile
    {
        public int Version { get; set; }
        public Dictionary<int, ExportedAnimDataEntry> Data { get; set; } = [];
    }

    private sealed class ExportedAnimDataEntry
    {
        public sbyte[] FrameData { get; set; } = new sbyte[64];
        public byte Unknown { get; set; }
        public byte FrameCount { get; set; }
        public byte FrameInterval { get; set; }
        public byte FrameStart { get; set; }
    }

    private const int EntryFrameCapacity = 64;
    private const int ExportVersion = 1;

    private readonly SortedDictionary<int, EntryData> _entries = [];
    private readonly SortedDictionary<int, EntryData> _originalEntries = [];
    private readonly HashSet<ushort> _dirtyIds = [];
    private readonly List<ushort> _entryIds = [];
    private int[] _headers = [];
    private byte[] _unknownTail = [];

    /// <summary>
    /// Gets the root path currently loaded into the browser service.
    /// </summary>
    public string LoadedRootPath { get; private set; } = string.Empty;

    /// <summary>
    /// Gets whether animdata is currently loaded and ready.
    /// </summary>
    public bool IsReady { get; private set; }

    /// <summary>
    /// Gets the last service status message.
    /// </summary>
    public string StatusMessage { get; private set; } = "Choose a Ultima data directory to start browsing animdata.";

    /// <summary>
    /// Gets the ids of all loaded animdata entries.
    /// </summary>
    public IReadOnlyList<ushort> EntryIds => _entryIds;

    /// <summary>
    /// Gets the number of staged animdata changes that have not yet been persisted.
    /// </summary>
    public int DirtyCount => _dirtyIds.Count;

    /// <summary>
    /// Loads animdata.mul from the provided root path.
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

        var animdataPath = Path.Combine(rootPath, "animdata.mul");
        if (!File.Exists(animdataPath))
        {
            StatusMessage = "animdata.mul is required before animdata can be browsed.";
            return;
        }

        try
        {
            LoadAnimData(animdataPath);
            LoadedRootPath = AssetWorkspaceService.NormalizePath(rootPath);
            IsReady = true;
            StatusMessage = $"Loaded {_entryIds.Count} animdata entries from {LoadedRootPath}.";
        }
        catch (Exception ex)
        {
            ResetState();
            StatusMessage = $"Failed to load animdata: {ex.Message}";
        }
    }

    /// <summary>
    /// Filters animdata ids using numeric text matching.
    /// </summary>
    public List<ushort> GetFilteredIds(string filterText)
    {
        var results = new List<ushort>();
        if (!IsReady)
        {
            return results;
        }

        if (string.IsNullOrWhiteSpace(filterText))
        {
            results.AddRange(_entryIds);
            return results;
        }

        foreach (var id in _entryIds)
        {
            if (id.ToString().Contains(filterText, StringComparison.InvariantCultureIgnoreCase) ||
                $"0x{id:X4}".Contains(filterText, StringComparison.InvariantCultureIgnoreCase))
            {
                results.Add(id);
            }
        }

        return results;
    }

    /// <summary>
    /// Gets one animdata entry snapshot.
    /// </summary>
    public AssetAnimDataEntry GetEntry(ushort baseId)
    {
        EnsureValidId(baseId);
        var entry = _entries[baseId];
        return new AssetAnimDataEntry(baseId, entry.Unknown, entry.FrameInterval, entry.FrameStart, [.. entry.FrameOffsets]);
    }

    /// <summary>
    /// Creates a new animdata entry when one does not already exist.
    /// </summary>
    public void AddEntry(ushort baseId)
    {
        EnsureReady();
        if (_entries.ContainsKey(baseId))
        {
            throw new InvalidOperationException("An animdata entry already exists for the selected base id.");
        }

        _entries.Add(baseId, new EntryData
        {
            Unknown = 0,
            FrameInterval = 0,
            FrameStart = 0,
            FrameOffsets = [0],
        });

        RebuildEntryIds();
        UpdateDirtyState(baseId);
    }

    /// <summary>
    /// Removes one animdata entry from the staged working set.
    /// </summary>
    public void RemoveEntry(ushort baseId)
    {
        EnsureValidId(baseId);
        _entries.Remove(baseId);
        RebuildEntryIds();
        UpdateDirtyState(baseId);
    }

    /// <summary>
    /// Applies edits to one animdata entry.
    /// </summary>
    public void UpdateEntry(ushort baseId, byte frameInterval, byte frameStart, IReadOnlyList<sbyte> frameOffsets)
    {
        EnsureValidId(baseId);
        if (frameOffsets.Count == 0)
        {
            throw new InvalidOperationException("Animdata entries require at least one frame.");
        }

        if (frameOffsets.Count > EntryFrameCapacity)
        {
            throw new InvalidOperationException($"Animdata entries cannot contain more than {EntryFrameCapacity} frames.");
        }

        var entry = _entries[baseId];
        entry.FrameInterval = frameInterval;
        entry.FrameStart = frameStart;
        entry.FrameOffsets = [.. frameOffsets];
        _entries[baseId] = entry;
        UpdateDirtyState(baseId);
    }

    /// <summary>
    /// Restores one entry from the original loaded file state.
    /// </summary>
    public void RevertEntry(ushort baseId)
    {
        EnsureReady();
        if (_originalEntries.TryGetValue(baseId, out var original))
        {
            _entries[baseId] = original.Clone();
        }
        else
        {
            _entries.Remove(baseId);
        }

        RebuildEntryIds();
        _dirtyIds.Remove(baseId);
    }

    /// <summary>
    /// Gets whether one entry currently has staged edits.
    /// </summary>
    public bool IsDirty(ushort baseId) => _dirtyIds.Contains(baseId);

    /// <summary>
    /// Saves all current animdata edits back into animdata.mul.
    /// </summary>
    public void Save()
    {
        EnsureReady();
        var animdataPath = Path.Combine(LoadedRootPath, "animdata.mul");
        var backupPath = animdataPath + ".bak";
        var tempPath = animdataPath + ".tmp";

        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }

        File.Copy(animdataPath, backupPath, true);

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                var id = 0;
                var headerIndex = 0;
                var maxId = _entries.Count == 0 ? 0 : _entries.Keys.Max();

                while (id <= maxId)
                {
                    var header = headerIndex < _headers.Length ? _headers[headerIndex++] : Random.Shared.Next();
                    writer.Write(header);

                    for (var i = 0; i < 8; i++, id++)
                    {
                        if (_entries.TryGetValue(id, out var entry))
                        {
                            WriteEntry(writer, entry);
                        }
                        else
                        {
                            WriteEmptyEntry(writer);
                        }
                    }
                }

                if (_unknownTail.Length > 0)
                {
                    writer.Write(_unknownTail);
                }
            }

            File.Copy(tempPath, animdataPath, true);
            File.Delete(tempPath);
            RefreshOriginalState();
            _dirtyIds.Clear();
            StatusMessage = $"Saved animdata to {animdataPath}.";
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
    /// Exports one or more animdata entries using UO Fiddler-compatible JSON.
    /// </summary>
    public int ExportJson(string outputPath, IReadOnlyCollection<ushort> ids)
    {
        EnsureReady();
        var file = new ExportedAnimDataFile
        {
            Version = ExportVersion,
            Data = ids
                .Where(id => _entries.ContainsKey(id))
                .ToDictionary(
                    id => (int)id,
                    id => ToExportedEntry(_entries[id]))
        };

        File.WriteAllText(outputPath, JsonSerializer.Serialize(file));
        return file.Data.Count;
    }

    /// <summary>
    /// Imports animdata entries using UO Fiddler-compatible JSON.
    /// </summary>
    public int ImportJson(string inputPath, bool overwriteExisting, bool eraseBeforeImport)
    {
        EnsureReady();
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("The selected animdata import file was not found.", inputPath);
        }

        var imported = JsonSerializer.Deserialize<ExportedAnimDataFile>(File.ReadAllText(inputPath))
                       ?? throw new InvalidOperationException("Imported null animdata payload.");

        if (imported.Version != ExportVersion)
        {
            throw new InvalidOperationException($"Unexpected version {imported.Version}, expected {ExportVersion}.");
        }

        if (eraseBeforeImport)
        {
            _entries.Clear();
        }

        var importedCount = 0;
        foreach (var (id, exportedEntry) in imported.Data)
        {
            if (id < 0 || id > ushort.MaxValue)
            {
                continue;
            }

            if (!overwriteExisting && _entries.ContainsKey(id))
            {
                continue;
            }

            var entry = FromExportedEntry(exportedEntry);
            if (entry.FrameOffsets.Count == 0)
            {
                continue;
            }

            _entries[id] = entry;
            UpdateDirtyState((ushort)id);
            importedCount++;
        }

        RebuildEntryIds();
        return importedCount;
    }

    private void LoadAnimData(string animdataPath)
    {
        using var stream = new FileStream(animdataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);

        var headerCount = (int)(stream.Length / (4 + (8 * (EntryFrameCapacity + 4))));
        _headers = new int[headerCount];
        _entries.Clear();

        var id = 0;
        for (var h = 0; h < _headers.Length; h++)
        {
            _headers[h] = reader.ReadInt32();

            for (var i = 0; i < 8; i++, id++)
            {
                var frameData = new sbyte[EntryFrameCapacity];
                for (var frameIndex = 0; frameIndex < frameData.Length; frameIndex++)
                {
                    frameData[frameIndex] = reader.ReadSByte();
                }

                var unknown = reader.ReadByte();
                var frameCount = reader.ReadByte();
                var frameInterval = reader.ReadByte();
                var frameStart = reader.ReadByte();

                if (frameCount > 0)
                {
                    var count = Math.Min(frameCount, (byte)EntryFrameCapacity);
                    _entries[id] = new EntryData
                    {
                        Unknown = unknown,
                        FrameInterval = frameInterval,
                        FrameStart = frameStart,
                        FrameOffsets = [.. frameData.Take(count)],
                    };
                }
            }
        }

        var remaining = (int)(stream.Length - stream.Position);
        _unknownTail = remaining > 0 ? reader.ReadBytes(remaining) : [];
        RefreshOriginalState();
        RebuildEntryIds();
    }

    private static void WriteEntry(BinaryWriter writer, EntryData entry)
    {
        for (var i = 0; i < EntryFrameCapacity; i++)
        {
            writer.Write(i < entry.FrameOffsets.Count ? entry.FrameOffsets[i] : (sbyte)0);
        }

        writer.Write(entry.Unknown);
        writer.Write((byte)entry.FrameOffsets.Count);
        writer.Write(entry.FrameInterval);
        writer.Write(entry.FrameStart);
    }

    private static void WriteEmptyEntry(BinaryWriter writer)
    {
        for (var i = 0; i < EntryFrameCapacity; i++)
        {
            writer.Write((sbyte)0);
        }

        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
    }

    private static ExportedAnimDataEntry ToExportedEntry(EntryData entry)
    {
        var frameData = new sbyte[EntryFrameCapacity];
        for (var i = 0; i < entry.FrameOffsets.Count; i++)
        {
            frameData[i] = entry.FrameOffsets[i];
        }

        return new ExportedAnimDataEntry
        {
            FrameData = frameData,
            Unknown = entry.Unknown,
            FrameCount = (byte)entry.FrameOffsets.Count,
            FrameInterval = entry.FrameInterval,
            FrameStart = entry.FrameStart,
        };
    }

    private static EntryData FromExportedEntry(ExportedAnimDataEntry exportedEntry)
    {
        var count = Math.Min(exportedEntry.FrameCount, (byte)EntryFrameCapacity);
        return new EntryData
        {
            Unknown = exportedEntry.Unknown,
            FrameInterval = exportedEntry.FrameInterval,
            FrameStart = exportedEntry.FrameStart,
            FrameOffsets = [.. exportedEntry.FrameData.Take(count)],
        };
    }

    private void RefreshOriginalState()
    {
        _originalEntries.Clear();
        foreach (var (id, entry) in _entries)
        {
            _originalEntries[id] = entry.Clone();
        }
    }

    private void RebuildEntryIds()
    {
        _entryIds.Clear();
        foreach (var id in _entries.Keys)
        {
            _entryIds.Add((ushort)id);
        }
    }

    private void UpdateDirtyState(ushort baseId)
    {
        var hasCurrent = _entries.TryGetValue(baseId, out var current);
        var hasOriginal = _originalEntries.TryGetValue(baseId, out var original);

        if (hasCurrent == hasOriginal)
        {
            if (!hasCurrent || AreEqual(current!, original!))
            {
                _dirtyIds.Remove(baseId);
                return;
            }
        }

        _dirtyIds.Add(baseId);
    }

    private static bool AreEqual(EntryData left, EntryData right)
    {
        return left.Unknown == right.Unknown &&
               left.FrameInterval == right.FrameInterval &&
               left.FrameStart == right.FrameStart &&
               left.FrameOffsets.SequenceEqual(right.FrameOffsets);
    }

    private void EnsureReady()
    {
        if (!IsReady)
        {
            throw new InvalidOperationException("The animdata service is not ready.");
        }
    }

    private void EnsureValidId(ushort baseId)
    {
        EnsureReady();
        if (!_entries.ContainsKey(baseId))
        {
            throw new InvalidOperationException("The selected animdata entry does not exist.");
        }
    }

    private void ResetState()
    {
        LoadedRootPath = string.Empty;
        IsReady = false;
        _entries.Clear();
        _originalEntries.Clear();
        _dirtyIds.Clear();
        _entryIds.Clear();
        _headers = [];
        _unknownTail = [];
    }
}