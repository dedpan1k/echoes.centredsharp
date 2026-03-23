using Microsoft.Xna.Framework.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Runtime.InteropServices;
using FNAColor = Microsoft.Xna.Framework.Color;
using Rectangle = System.Drawing.Rectangle;

namespace CentrED.Assets;

/// <summary>
/// Preview metadata for one locally loaded gump entry.
/// </summary>
public readonly record struct AssetGumpPreview(
    ushort GumpId,
    Texture2D? Texture,
    Rectangle Bounds,
    int Width,
    int Height)
{
    /// <summary>
    /// Sentinel returned when a gump preview cannot be produced.
    /// </summary>
    public static AssetGumpPreview Invalid => new(0, null, default, 0, 0);

    /// <summary>
    /// Gets whether the preview contains a valid source texture region.
    /// </summary>
    public bool IsValid => Texture != null && Bounds.Width > 0 && Bounds.Height > 0;
}

/// <summary>
/// Loads local gump assets from legacy gumpidx.mul and gumpart.mul files for the asset workspace.
/// </summary>
public sealed class AssetGumpBrowserService
{
    private sealed record StagedReplacement(Texture2D Texture, Rectangle Bounds, string SourcePath, int Width, int Height);
    private sealed record CachedPreview(Texture2D Texture, Rectangle Bounds, int Width, int Height);
    private readonly record struct SerializedGumpImage(int Width, int Height, ushort[] Pixels);
    private readonly record struct GumpIndexEntry(int Lookup, int Length, int Width, int Height)
    {
        public bool IsValid => Lookup >= 0 && Length > 0 && Width > 0 && Height > 0;
    }

    private GraphicsDevice? _graphicsDevice;
    private readonly List<ushort> _validGumpIds = [];
    private readonly Dictionary<ushort, StagedReplacement> _stagedReplacements = new();
    private readonly HashSet<ushort> _stagedRemovals = [];
    private readonly Dictionary<ushort, GumpIndexEntry> _entries = new();
    private readonly Dictionary<ushort, CachedPreview> _cachedPreviews = new();

    /// <summary>
    /// Gets the root path currently loaded into the browser service.
    /// </summary>
    public string LoadedRootPath { get; private set; } = string.Empty;

    /// <summary>
    /// Gets whether the local gump files are currently loaded and ready.
    /// </summary>
    public bool IsReady { get; private set; }

    /// <summary>
    /// Gets the last service status message.
    /// </summary>
    public string StatusMessage { get; private set; } = "Choose a Ultima data directory to start browsing gumps.";

    /// <summary>
    /// Gets the valid gump ids discovered in the currently loaded client directory.
    /// </summary>
    public IReadOnlyList<ushort> ValidGumpIds => _validGumpIds;

    /// <summary>
    /// Gets the number of staged local gump changes that have not yet been persisted.
    /// </summary>
    public int DirtyCount => _stagedReplacements.Count + _stagedRemovals.Count;

    /// <summary>
    /// Ensures the browser service is loaded for the provided client directory.
    /// </summary>
    public void EnsureLoaded(GraphicsDevice graphicsDevice, string rootPath)
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

        var idxPath = Path.Combine(rootPath, "gumpidx.mul");
        var mulPath = Path.Combine(rootPath, "gumpart.mul");
        if (!File.Exists(idxPath) || !File.Exists(mulPath))
        {
            StatusMessage = "The current gump editor slice requires legacy gumpidx.mul and gumpart.mul files.";
            return;
        }

        try
        {
            using var stream = new FileStream(idxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream);
            while (stream.Position <= stream.Length - 12)
            {
                var index = (int)(stream.Position / 12);
                var lookup = reader.ReadInt32();
                var length = reader.ReadInt32();
                var extra = reader.ReadInt32();
                var width = (extra >> 16) & 0xFFFF;
                var height = extra & 0xFFFF;
                if (lookup >= 0 && length > 0 && width > 0 && height > 0)
                {
                    _entries[(ushort)index] = new GumpIndexEntry(lookup, length, width, height);
                    _validGumpIds.Add((ushort)index);
                }
            }

            _graphicsDevice = graphicsDevice;
            LoadedRootPath = AssetWorkspaceService.NormalizePath(rootPath);
            IsReady = true;
            StatusMessage = $"Loaded {_validGumpIds.Count} gump entries from {LoadedRootPath}.";
        }
        catch (Exception ex)
        {
            ResetState();
            StatusMessage = $"Failed to load local gump assets: {ex.Message}";
        }
    }

    /// <summary>
    /// Filters the available gump ids using numeric text matching.
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
            results.AddRange(_validGumpIds);
            return results;
        }

        foreach (var id in _validGumpIds)
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
    /// Builds a gump preview entry for display in the asset workspace.
    /// </summary>
    public AssetGumpPreview GetPreview(ushort gumpId)
    {
        if (!IsReady || _graphicsDevice == null)
        {
            return AssetGumpPreview.Invalid;
        }

        if (_stagedRemovals.Contains(gumpId))
        {
            return AssetGumpPreview.Invalid;
        }

        if (_stagedReplacements.TryGetValue(gumpId, out var replacement))
        {
            return new AssetGumpPreview(gumpId, replacement.Texture, replacement.Bounds, replacement.Width, replacement.Height);
        }

        if (_cachedPreviews.TryGetValue(gumpId, out var cachedPreview))
        {
            return new AssetGumpPreview(gumpId, cachedPreview.Texture, cachedPreview.Bounds, cachedPreview.Width, cachedPreview.Height);
        }

        if (!_entries.TryGetValue(gumpId, out var entry) || !entry.IsValid)
        {
            return AssetGumpPreview.Invalid;
        }

        return TryLoadOriginalPreview(gumpId, entry, out var preview)
            ? preview
            : AssetGumpPreview.Invalid;
    }

    /// <summary>
    /// Gets whether one gump currently has a staged change.
    /// </summary>
    public bool IsDirty(ushort gumpId)
    {
        return _stagedReplacements.ContainsKey(gumpId) || _stagedRemovals.Contains(gumpId);
    }

    /// <summary>
    /// Gets whether one gump is staged for removal on the next save.
    /// </summary>
    public bool IsMarkedForRemoval(ushort gumpId)
    {
        return _stagedRemovals.Contains(gumpId);
    }

    /// <summary>
    /// Returns the source path for a staged replacement when present.
    /// </summary>
    public string GetReplacementSourcePath(ushort gumpId)
    {
        return _stagedReplacements.TryGetValue(gumpId, out var replacement)
            ? replacement.SourcePath
            : string.Empty;
    }

    /// <summary>
    /// Exports the currently visible preview for one gump to an image file.
    /// </summary>
    public void ExportPreview(ushort gumpId, string outputPath)
    {
        var preview = GetPreview(gumpId);
        if (!preview.IsValid || preview.Texture == null)
        {
            throw new InvalidOperationException("No preview is available for the selected gump.");
        }

        var pixels = new FNAColor[preview.Bounds.Width * preview.Bounds.Height];
        preview.Texture.GetData(
            0,
            new Microsoft.Xna.Framework.Rectangle(preview.Bounds.X, preview.Bounds.Y, preview.Bounds.Width, preview.Bounds.Height),
            pixels,
            0,
            pixels.Length);

        using var image = new Image<Rgba32>(preview.Bounds.Width, preview.Bounds.Height);
        for (var y = 0; y < preview.Bounds.Height; y++)
        {
            for (var x = 0; x < preview.Bounds.Width; x++)
            {
                var pixel = pixels[y * preview.Bounds.Width + x];
                image[x, y] = new Rgba32(pixel.R, pixel.G, pixel.B, pixel.A);
            }
        }

        image.Save(outputPath);
    }

    /// <summary>
    /// Loads a replacement image and stages it as an in-memory override for one gump preview.
    /// </summary>
    public void StageReplacement(ushort gumpId, string imagePath)
    {
        if (_graphicsDevice == null)
        {
            throw new InvalidOperationException("Gump graphics device is not ready.");
        }

        using var image = Image.Load<Rgba32>(imagePath);
        if (image.Width <= 0 || image.Height <= 0)
        {
            throw new InvalidOperationException("Gump replacements must have a positive size.");
        }

        var pixels = new FNAColor[image.Width * image.Height];
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                pixels[y * image.Width + x] = new FNAColor(pixel.R, pixel.G, pixel.B, pixel.A);
            }
        }

        var texture = new Texture2D(_graphicsDevice, image.Width, image.Height);
        texture.SetData(pixels);

        if (_stagedReplacements.Remove(gumpId, out var existing))
        {
            existing.Texture.Dispose();
        }

        _stagedRemovals.Remove(gumpId);
        _stagedReplacements[gumpId] = new StagedReplacement(texture, new Rectangle(0, 0, image.Width, image.Height), imagePath, image.Width, image.Height);

        if (!_validGumpIds.Contains(gumpId))
        {
            _validGumpIds.Add(gumpId);
            _validGumpIds.Sort();
        }
    }

    /// <summary>
    /// Stages one gump entry for removal from gumpart.mul on the next save.
    /// </summary>
    public void StageRemoval(ushort gumpId)
    {
        if (_stagedReplacements.Remove(gumpId, out var replacement))
        {
            replacement.Texture.Dispose();
        }

        _stagedRemovals.Add(gumpId);
    }

    /// <summary>
    /// Clears any staged replacement or removal for one gump.
    /// </summary>
    public void ClearStagedChange(ushort gumpId)
    {
        if (_stagedReplacements.Remove(gumpId, out var replacement))
        {
            replacement.Texture.Dispose();
        }

        _stagedRemovals.Remove(gumpId);
    }

    /// <summary>
    /// Saves the current staged gump changes into gumpart.mul and gumpidx.mul.
    /// Existing files are copied to .bak before the new archives are written.
    /// </summary>
    public void SaveStagedChanges()
    {
        if (!IsReady || _graphicsDevice == null)
        {
            throw new InvalidOperationException("Local gump assets are not loaded.");
        }

        if (DirtyCount == 0)
        {
            throw new InvalidOperationException("There are no staged gump changes to save.");
        }

        var rootPath = LoadedRootPath;
        if (rootPath.Length == 0 || !Directory.Exists(rootPath))
        {
            throw new InvalidOperationException("The selected Ultima data directory is no longer available.");
        }

        var idxPath = Path.Combine(rootPath, "gumpidx.mul");
        var mulPath = Path.Combine(rootPath, "gumpart.mul");
        var tempIdxPath = idxPath + ".tmp";
        var tempMulPath = mulPath + ".tmp";
        var stagedCount = DirtyCount;
        var maxIndex = DetermineSaveUpperBound();

        try
        {
            using (var idxStream = new FileStream(tempIdxPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var mulStream = new FileStream(tempMulPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var idxWriter = new BinaryWriter(idxStream))
            using (var mulWriter = new BinaryWriter(mulStream))
            {
                for (var index = 0; index < maxIndex; index++)
                {
                    if (!TryGetImageForSave((ushort)index, out var imageData))
                    {
                        idxWriter.Write(-1);
                        idxWriter.Write(0);
                        idxWriter.Write(0);
                        continue;
                    }

                    var entryStart = checked((int)mulWriter.BaseStream.Position);
                    var rowLookupStart = entryStart;
                    for (var row = 0; row < imageData.Height; row++)
                    {
                        mulWriter.Write(0);
                    }

                    for (var y = 0; y < imageData.Height; y++)
                    {
                        var currentPosition = checked((int)mulWriter.BaseStream.Position);
                        var offset = (currentPosition - rowLookupStart) / 4;
                        var returnPosition = mulWriter.BaseStream.Position;
                        mulWriter.BaseStream.Seek(rowLookupStart + (y * sizeof(int)), SeekOrigin.Begin);
                        mulWriter.Write(offset);
                        mulWriter.BaseStream.Seek(returnPosition, SeekOrigin.Begin);

                        var rowStart = y * imageData.Width;
                        var x = 0;
                        while (x < imageData.Width)
                        {
                            var color = imageData.Pixels[rowStart + x];
                            var run = 1;
                            while (x + run < imageData.Width && imageData.Pixels[rowStart + x + run] == color)
                            {
                                run++;
                            }

                            mulWriter.Write(color == 0 ? (ushort)0 : (ushort)(color ^ 0x8000));
                            mulWriter.Write((ushort)run);
                            x += run;
                        }
                    }

                    var entryLength = checked((int)mulWriter.BaseStream.Position) - entryStart;
                    idxWriter.Write(entryStart);
                    idxWriter.Write(entryLength);
                    idxWriter.Write((imageData.Width << 16) + imageData.Height);
                }
            }

            BackupIfPresent(idxPath);
            BackupIfPresent(mulPath);
            File.Move(tempIdxPath, idxPath, true);
            File.Move(tempMulPath, mulPath, true);
        }
        finally
        {
            if (File.Exists(tempIdxPath))
            {
                File.Delete(tempIdxPath);
            }

            if (File.Exists(tempMulPath))
            {
                File.Delete(tempMulPath);
            }
        }

        var graphicsDevice = _graphicsDevice;
        ResetState();
        EnsureLoaded(graphicsDevice, rootPath);
        StatusMessage = $"Saved {stagedCount} staged gump change(s) to {mulPath}.";
    }

    private int DetermineSaveUpperBound()
    {
        var upper = _validGumpIds.Count == 0 ? 0 : _validGumpIds[^1] + 1;
        if (_entries.Count > 0)
        {
            upper = Math.Max(upper, _entries.Keys.Max() + 1);
        }

        if (_stagedReplacements.Count > 0)
        {
            upper = Math.Max(upper, _stagedReplacements.Keys.Max() + 1);
        }

        if (_stagedRemovals.Count > 0)
        {
            upper = Math.Max(upper, _stagedRemovals.Max() + 1);
        }

        return upper;
    }

    private bool TryGetImageForSave(ushort gumpId, out SerializedGumpImage imageData)
    {
        imageData = default;

        if (_stagedRemovals.Contains(gumpId))
        {
            return false;
        }

        if (_stagedReplacements.TryGetValue(gumpId, out var replacement))
        {
            imageData = BuildSerializedImage(replacement.Texture, replacement.Bounds, replacement.Width, replacement.Height);
            return true;
        }

        if (!_entries.TryGetValue(gumpId, out var entry) || !entry.IsValid)
        {
            return false;
        }

        return TryLoadOriginalImage(entry, out imageData);
    }

    private bool TryLoadOriginalPreview(ushort gumpId, GumpIndexEntry entry, out AssetGumpPreview preview)
    {
        preview = AssetGumpPreview.Invalid;
        if (!TryLoadOriginalImage(entry, out var imageData) || _graphicsDevice == null)
        {
            return false;
        }

        var texture = new Texture2D(_graphicsDevice, imageData.Width, imageData.Height);
        var pixels = new FNAColor[imageData.Pixels.Length];
        for (var i = 0; i < imageData.Pixels.Length; i++)
        {
            pixels[i] = UnpackArgb1555(imageData.Pixels[i]);
        }

        texture.SetData(pixels);
        var bounds = new Rectangle(0, 0, imageData.Width, imageData.Height);
        _cachedPreviews[gumpId] = new CachedPreview(texture, bounds, imageData.Width, imageData.Height);
        preview = new AssetGumpPreview(gumpId, texture, bounds, imageData.Width, imageData.Height);
        return true;
    }

    private bool TryLoadOriginalImage(GumpIndexEntry entry, out SerializedGumpImage imageData)
    {
        imageData = default;
        var mulPath = Path.Combine(LoadedRootPath, "gumpart.mul");
        if (!File.Exists(mulPath))
        {
            return false;
        }

        using var stream = new FileStream(mulPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(stream);
        if (entry.Lookup < 0 || entry.Lookup >= stream.Length)
        {
            return false;
        }

        stream.Seek(entry.Lookup, SeekOrigin.Begin);
        var rowOffsets = new int[entry.Height];
        for (var i = 0; i < entry.Height; i++)
        {
            rowOffsets[i] = reader.ReadInt32();
        }

        var data = reader.ReadBytes(entry.Length - (entry.Height * sizeof(int)));
        var words = MemoryMarshal.Cast<byte, ushort>(data).ToArray();
        var pixels = new ushort[entry.Width * entry.Height];

        for (var y = 0; y < entry.Height; y++)
        {
            var position = rowOffsets[y] * 2;
            var x = 0;
            while (x < entry.Width && position + 1 < words.Length)
            {
                var color = words[position++];
                var run = words[position++];
                var decoded = color == 0 ? (ushort)0 : (ushort)(color ^ 0x8000);
                for (var step = 0; step < run && x < entry.Width; step++, x++)
                {
                    pixels[(y * entry.Width) + x] = decoded;
                }
            }
        }

        imageData = new SerializedGumpImage(entry.Width, entry.Height, pixels);
        return true;
    }

    private static SerializedGumpImage BuildSerializedImage(Texture2D texture, Rectangle bounds, int width, int height)
    {
        var colors = new FNAColor[width * height];
        texture.GetData(
            0,
            new Microsoft.Xna.Framework.Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            colors,
            0,
            colors.Length);

        var pixels = new ushort[colors.Length];
        for (var i = 0; i < colors.Length; i++)
        {
            pixels[i] = PackArgb1555(colors[i]);
        }

        return new SerializedGumpImage(width, height, pixels);
    }

    private static ushort PackArgb1555(FNAColor color)
    {
        if (color.A < 128)
        {
            return 0;
        }

        var red = (ushort)(color.R >> 3);
        var green = (ushort)(color.G >> 3);
        var blue = (ushort)(color.B >> 3);
        return (ushort)(0x8000 | (red << 10) | (green << 5) | blue);
    }

    private static FNAColor UnpackArgb1555(ushort value)
    {
        if (value == 0)
        {
            return new FNAColor(0, 0, 0, 0);
        }

        var red = (byte)(((value >> 10) & 0x1F) * 255 / 31);
        var green = (byte)(((value >> 5) & 0x1F) * 255 / 31);
        var blue = (byte)((value & 0x1F) * 255 / 31);
        return new FNAColor(red, green, blue, 255);
    }

    private static void BackupIfPresent(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Copy(filePath, filePath + ".bak", true);
        }
    }

    private void ResetState()
    {
        foreach (var replacement in _stagedReplacements.Values)
        {
            replacement.Texture.Dispose();
        }

        foreach (var preview in _cachedPreviews.Values)
        {
            preview.Texture.Dispose();
        }

        _stagedReplacements.Clear();
        _stagedRemovals.Clear();
        _validGumpIds.Clear();
        _entries.Clear();
        _cachedPreviews.Clear();
        _graphicsDevice = null;
        LoadedRootPath = string.Empty;
        IsReady = false;
    }
}