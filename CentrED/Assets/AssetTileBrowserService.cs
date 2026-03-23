using ClassicUO.Assets;
using ClassicUO.IO;
using ClassicUO.Renderer.Arts;
using ClassicUO.Renderer.Texmaps;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FNAColor = Microsoft.Xna.Framework.Color;
using Rectangle = System.Drawing.Rectangle;

namespace CentrED.Assets;

/// <summary>
/// Preview metadata for one locally loaded land or static asset entry.
/// </summary>
/// <param name="AssetId">The logical land or static asset id.</param>
/// <param name="RealIndex">The real art-table index, including the land/static offset when relevant.</param>
/// <param name="Texture">The cached texture atlas containing the preview sprite.</param>
/// <param name="Bounds">The atlas bounds used for the preview image.</param>
/// <param name="Name">The tiledata name associated with the asset.</param>
/// <param name="Flags">The tiledata flags formatted for display.</param>
/// <param name="Height">The static height, or zero for land entries.</param>
/// <param name="UsesTexmap">Whether the preview comes from texmaps rather than land art.</param>
public readonly record struct AssetTilePreview(
    ushort AssetId,
    int RealIndex,
    Texture2D? Texture,
    Rectangle Bounds,
    string Name,
    string Flags,
    uint Height,
    bool UsesTexmap)
{
    /// <summary>
    /// Sentinel returned when no preview could be produced for a requested entry.
    /// </summary>
    public static AssetTilePreview Invalid => new(0, -1, null, default, string.Empty, string.Empty, 0, false);

    /// <summary>
    /// Gets whether the preview contains a valid atlas rectangle and texture.
    /// </summary>
    public bool IsValid => Texture != null && Bounds.Width > 0 && Bounds.Height > 0;
}

/// <summary>
/// Loads local land and static art resources directly from a Ultima client directory for the asset workspace.
/// </summary>
public sealed class AssetTileBrowserService
{
    private const int LandTileSize = 44;

    private sealed record StagedReplacement(Texture2D Texture, Rectangle Bounds, string SourcePath);
    private readonly record struct SerializedArtImage(int Width, int Height, ushort[] Pixels, bool IsStatic);

    private UOFileManager? _uoFileManager;
    private Art? _arts;
    private Texmap? _texmaps;
    private GraphicsDevice? _graphicsDevice;
    private readonly List<ushort> _validLandIds = [];
    private readonly List<ushort> _validStaticIds = [];
    private readonly Dictionary<(bool ObjectMode, ushort AssetId), StagedReplacement> _stagedReplacements = new();
    private readonly HashSet<(bool ObjectMode, ushort AssetId)> _stagedRemovals = [];

    /// <summary>
    /// Gets the root path currently loaded into the browser service.
    /// </summary>
    public string LoadedRootPath { get; private set; } = string.Empty;

    /// <summary>
    /// Gets whether the local asset files are currently loaded and ready.
    /// </summary>
    public bool IsReady { get; private set; }

    /// <summary>
    /// Gets the last service status message.
    /// </summary>
    public string StatusMessage { get; private set; } = "Choose a Ultima data directory to start browsing art and land tiles.";

    /// <summary>
    /// Gets the valid land ids discovered in the currently loaded client directory.
    /// </summary>
    public IReadOnlyList<ushort> ValidLandIds => _validLandIds;

    /// <summary>
    /// Gets the valid static ids discovered in the currently loaded client directory.
    /// </summary>
    public IReadOnlyList<ushort> ValidStaticIds => _validStaticIds;

    /// <summary>
    /// Gets the number of staged local replacements that have not yet been persisted to asset archives.
    /// </summary>
    public int DirtyCount => _stagedReplacements.Count + _stagedRemovals.Count;

    /// <summary>
    /// Saves the current staged replacements into art.mul and artidx.mul.
    /// Existing files are copied to .bak before the new archives are written.
    /// </summary>
    public void SaveStagedReplacements()
    {
        if (!IsReady || _uoFileManager == null || _arts == null || _graphicsDevice == null)
        {
            throw new InvalidOperationException("Local art assets are not loaded.");
        }

        if (DirtyCount == 0)
        {
            throw new InvalidOperationException("There are no staged art changes to save.");
        }

        var rootPath = LoadedRootPath;
        if (rootPath.Length == 0 || !Directory.Exists(rootPath))
        {
            throw new InvalidOperationException("The selected Ultima data directory is no longer available.");
        }

        ValidateStagedReplacements();

        var stagedCount = DirtyCount;
        var artIdxPath = Path.Combine(rootPath, "artidx.mul");
        var artMulPath = Path.Combine(rootPath, "art.mul");
        var tempIdxPath = artIdxPath + ".tmp";
        var tempMulPath = artMulPath + ".tmp";

        try
        {
            using (var idxStream = new FileStream(tempIdxPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var mulStream = new FileStream(tempMulPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var idxWriter = new BinaryWriter(idxStream))
            using (var mulWriter = new BinaryWriter(mulStream))
            {
                var landCache = new Dictionary<string, (int Position, int Length)>(StringComparer.Ordinal);
                var staticCache = new Dictionary<string, (int Position, int Length)>(StringComparer.Ordinal);
                var totalEntryCount = ArtLoader.MAX_LAND_DATA_INDEX_COUNT + _uoFileManager.TileData.StaticData.Length;

                for (var index = 0; index < totalEntryCount; index++)
                {
                    if (!TryGetImageForSave(index, out var imageData))
                    {
                        idxWriter.Write(-1);
                        idxWriter.Write(0);
                        idxWriter.Write(-1);
                        continue;
                    }

                    var checksum = BuildChecksumKey(imageData);
                    var cache = imageData.IsStatic ? staticCache : landCache;
                    if (cache.TryGetValue(checksum, out var cachedEntry))
                    {
                        idxWriter.Write(cachedEntry.Position);
                        idxWriter.Write(cachedEntry.Length);
                        idxWriter.Write(0);
                        continue;
                    }

                    var entryStart = checked((int)mulWriter.BaseStream.Position);
                    if (imageData.IsStatic)
                    {
                        WriteStaticEntry(mulWriter, imageData);
                    }
                    else
                    {
                        WriteLandEntry(mulWriter, imageData);
                    }

                    var entryLength = checked((int)mulWriter.BaseStream.Position) - entryStart;
                    idxWriter.Write(entryStart);
                    idxWriter.Write(entryLength);
                    idxWriter.Write(0);
                    cache[checksum] = (entryStart, entryLength);
                }
            }

            BackupIfPresent(artIdxPath);
            BackupIfPresent(artMulPath);
            File.Move(tempIdxPath, artIdxPath, true);
            File.Move(tempMulPath, artMulPath, true);
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
        StatusMessage = $"Saved {stagedCount} staged replacement(s) to {artMulPath}.";
    }

    /// <summary>
    /// Ensures the browser service is loaded for the provided client directory.
    /// </summary>
    /// <param name="graphicsDevice">The graphics device used to build preview textures.</param>
    /// <param name="rootPath">The client directory to load.</param>
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

        var tiledataPath = Path.Combine(rootPath, "tiledata.mul");
        if (!File.Exists(tiledataPath))
        {
            StatusMessage = "tiledata.mul is required before land and static art can be browsed.";
            return;
        }

        try
        {
            var clientVersion = new FileInfo(tiledataPath).Length switch
            {
                >= 3188736 => ClientVersion.CV_7090,
                >= 1644544 => ClientVersion.CV_7000,
                _ => ClientVersion.CV_6000,
            };

            _uoFileManager = new UOFileManager(clientVersion, rootPath);
            _uoFileManager.Arts.Load();
            _uoFileManager.Hues.Load();
            _uoFileManager.TileData.Load();
            _uoFileManager.Texmaps.Load();

            _arts = new Art(_uoFileManager.Arts, _uoFileManager.Hues, graphicsDevice);
            _texmaps = new Texmap(_uoFileManager.Texmaps, graphicsDevice);
            _graphicsDevice = graphicsDevice;

            PopulateValidIds();

            LoadedRootPath = AssetWorkspaceService.NormalizePath(rootPath);
            IsReady = true;
            StatusMessage = $"Loaded {_validLandIds.Count} land entries and {_validStaticIds.Count} static entries from {LoadedRootPath}.";
        }
        catch (Exception ex)
        {
            ResetState();
            StatusMessage = $"Failed to load local art assets: {ex.Message}";
        }
    }

    /// <summary>
    /// Filters the available ids using name and numeric text matching.
    /// </summary>
    /// <param name="objectMode">Whether to filter static ids instead of land ids.</param>
    /// <param name="filterText">The text filter entered by the user.</param>
    /// <returns>The filtered ids in display order.</returns>
    public List<ushort> GetFilteredIds(bool objectMode, string filterText)
    {
        var results = new List<ushort>();
        if (!IsReady || _uoFileManager == null)
        {
            return results;
        }

        var source = objectMode ? _validStaticIds : _validLandIds;
        if (string.IsNullOrWhiteSpace(filterText))
        {
            results.AddRange(source);
            return results;
        }

        foreach (var id in source)
        {
            var name = objectMode
                ? _uoFileManager.TileData.StaticData[id].Name ?? string.Empty
                : _uoFileManager.TileData.LandData[id].Name ?? string.Empty;

            if (name.Contains(filterText, StringComparison.InvariantCultureIgnoreCase) ||
                id.ToString().Contains(filterText, StringComparison.InvariantCultureIgnoreCase) ||
                $"0x{id:X4}".Contains(filterText, StringComparison.InvariantCultureIgnoreCase))
            {
                results.Add(id);
            }
        }

        return results;
    }

    /// <summary>
    /// Builds a land or static preview entry for display in the asset workspace.
    /// </summary>
    /// <param name="objectMode">Whether to load a static preview instead of land art.</param>
    /// <param name="assetId">The local asset id to preview.</param>
    /// <param name="preferTexmaps">Whether land previews should prefer texmaps when both sources exist.</param>
    /// <returns>The generated preview entry, or <see cref="AssetTilePreview.Invalid"/> if unavailable.</returns>
    public AssetTilePreview GetPreview(bool objectMode, ushort assetId, bool preferTexmaps)
    {
        if (!IsReady || _uoFileManager == null || _arts == null || _texmaps == null)
        {
            return AssetTilePreview.Invalid;
        }

        if (_stagedReplacements.TryGetValue((objectMode, assetId), out var replacement))
        {
            return BuildReplacementPreview(objectMode, assetId, replacement);
        }

        return objectMode ? GetStaticPreview(assetId) : GetLandPreview(assetId, preferTexmaps);
    }

    /// <summary>
    /// Gets whether one asset currently has a staged local replacement.
    /// </summary>
    /// <param name="objectMode">Whether the asset is a static entry.</param>
    /// <param name="assetId">The asset id to query.</param>
    /// <returns><c>true</c> if the asset has a staged replacement; otherwise, <c>false</c>.</returns>
    public bool IsDirty(bool objectMode, ushort assetId)
    {
        return _stagedReplacements.ContainsKey((objectMode, assetId)) || _stagedRemovals.Contains((objectMode, assetId));
    }

    /// <summary>
    /// Gets whether one asset is staged for removal on the next save.
    /// </summary>
    public bool IsMarkedForRemoval(bool objectMode, ushort assetId)
    {
        return _stagedRemovals.Contains((objectMode, assetId));
    }

    /// <summary>
    /// Returns the source path for a staged replacement when present.
    /// </summary>
    /// <param name="objectMode">Whether the asset is a static entry.</param>
    /// <param name="assetId">The asset id to query.</param>
    /// <returns>The staged source image path, or an empty string if none exists.</returns>
    public string GetReplacementSourcePath(bool objectMode, ushort assetId)
    {
        return _stagedReplacements.TryGetValue((objectMode, assetId), out var replacement)
            ? replacement.SourcePath
            : string.Empty;
    }

    /// <summary>
    /// Exports the currently visible preview for one asset to an image file.
    /// </summary>
    /// <param name="objectMode">Whether the asset is a static entry.</param>
    /// <param name="assetId">The asset id to export.</param>
    /// <param name="preferTexmaps">Whether land previews should prefer texmaps.</param>
    /// <param name="outputPath">The destination file path.</param>
    public void ExportPreview(bool objectMode, ushort assetId, bool preferTexmaps, string outputPath)
    {
        var preview = GetPreview(objectMode, assetId, preferTexmaps);
        if (!preview.IsValid || preview.Texture == null)
        {
            throw new InvalidOperationException("No preview is available for the selected asset.");
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
    /// Loads a replacement image and stages it as an in-memory override for one asset preview.
    /// </summary>
    /// <param name="objectMode">Whether the asset is a static entry.</param>
    /// <param name="assetId">The asset id to replace.</param>
    /// <param name="imagePath">The source image path.</param>
    public void StageReplacement(bool objectMode, ushort assetId, string imagePath)
    {
        if (_graphicsDevice == null)
        {
            throw new InvalidOperationException("Asset graphics device is not ready.");
        }

        using var image = Image.Load<Rgba32>(imagePath);
        if (!objectMode && (image.Width != LandTileSize || image.Height != LandTileSize))
        {
            throw new InvalidOperationException("Land tile replacements must be 44x44 images.");
        }

        if (image.Width <= 0 || image.Height <= 0)
        {
            throw new InvalidOperationException("Replacement images must have a positive size.");
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

        if (_stagedReplacements.TryGetValue((objectMode, assetId), out var existing))
        {
            existing.Texture.Dispose();
        }

        _stagedRemovals.Remove((objectMode, assetId));
        _stagedReplacements[(objectMode, assetId)] = new StagedReplacement(texture, new Rectangle(0, 0, image.Width, image.Height), imagePath);
    }

    /// <summary>
    /// Stages one asset entry for removal from art.mul on the next save.
    /// </summary>
    public void StageRemoval(bool objectMode, ushort assetId)
    {
        if (_stagedReplacements.Remove((objectMode, assetId), out var replacement))
        {
            replacement.Texture.Dispose();
        }

        _stagedRemovals.Add((objectMode, assetId));
    }

    /// <summary>
    /// Clears any staged replacement or removal for one asset.
    /// </summary>
    /// <param name="objectMode">Whether the asset is a static entry.</param>
    /// <param name="assetId">The asset id to reset.</param>
    public void ClearStagedChange(bool objectMode, ushort assetId)
    {
        if (_stagedReplacements.Remove((objectMode, assetId), out var replacement))
        {
            replacement.Texture.Dispose();
        }

        _stagedRemovals.Remove((objectMode, assetId));
    }

    private void ValidateStagedReplacements()
    {
        foreach (var ((objectMode, assetId), replacement) in _stagedReplacements)
        {
            if (!objectMode && (replacement.Bounds.Width != LandTileSize || replacement.Bounds.Height != LandTileSize))
            {
                throw new InvalidOperationException($"Land tile {assetId:X4} must be 44x44 before it can be saved.");
            }

            if (replacement.Bounds.Width <= 0 || replacement.Bounds.Height <= 0)
            {
                throw new InvalidOperationException($"Asset {assetId:X4} has an invalid staged replacement size.");
            }
        }
    }

    private static void BackupIfPresent(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Copy(filePath, filePath + ".bak", true);
        }
    }

    private static string BuildChecksumKey(SerializedArtImage imageData)
    {
        var checksum = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(imageData.Pixels.AsSpan())));
        return imageData.IsStatic
            ? $"{imageData.Width}x{imageData.Height}:{checksum}"
            : checksum;
    }

    private bool TryGetImageForSave(int realIndex, out SerializedArtImage imageData)
    {
        var objectMode = realIndex >= ArtLoader.MAX_LAND_DATA_INDEX_COUNT;
        var assetId = (ushort)(objectMode ? realIndex - ArtLoader.MAX_LAND_DATA_INDEX_COUNT : realIndex);

        if (_stagedRemovals.Contains((objectMode, assetId)))
        {
            imageData = default;
            return false;
        }

        if (_stagedReplacements.TryGetValue((objectMode, assetId), out var replacement))
        {
            imageData = BuildSerializedImage(replacement.Texture, replacement.Bounds, objectMode);
            return true;
        }

        return objectMode
            ? TryGetOriginalStaticImage(assetId, out imageData)
            : TryGetOriginalLandImage(assetId, out imageData);
    }

    private bool TryGetOriginalLandImage(ushort assetId, out SerializedArtImage imageData)
    {
        imageData = default;
        if (_uoFileManager == null || _arts == null)
        {
            return false;
        }

        if (_uoFileManager.Arts.File.GetValidRefEntry(assetId).Length <= 0)
        {
            return false;
        }

        var spriteInfo = _arts.GetLand(assetId);
        if (spriteInfo.Texture == null || spriteInfo.UV.Width <= 0 || spriteInfo.UV.Height <= 0)
        {
            return false;
        }

        imageData = BuildSerializedImage(
            spriteInfo.Texture,
            new Rectangle(spriteInfo.UV.X, spriteInfo.UV.Y, spriteInfo.UV.Width, spriteInfo.UV.Height),
            false);
        return true;
    }

    private bool TryGetOriginalStaticImage(ushort assetId, out SerializedArtImage imageData)
    {
        imageData = default;
        if (_uoFileManager == null || _arts == null)
        {
            return false;
        }

        ref var entry = ref _uoFileManager.Arts.File.GetValidRefEntry(assetId + ArtLoader.MAX_LAND_DATA_INDEX_COUNT);
        if (entry.Equals(UOFileIndex.Invalid) || entry.Length <= 0)
        {
            return false;
        }

        var realIndex = assetId + entry.AnimOffset;
        var spriteInfo = _arts.GetArt((uint)realIndex);
        if (spriteInfo.Texture == null)
        {
            return false;
        }

        var realBounds = _arts.GetRealArtBounds((uint)realIndex);
        if (realBounds.Width <= 0 || realBounds.Height <= 0)
        {
            return false;
        }

        imageData = BuildSerializedImage(
            spriteInfo.Texture,
            new Rectangle(spriteInfo.UV.X + realBounds.X, spriteInfo.UV.Y + realBounds.Y, realBounds.Width, realBounds.Height),
            true);
        return true;
    }

    private static SerializedArtImage BuildSerializedImage(Texture2D texture, Rectangle bounds, bool isStatic)
    {
        var colors = new FNAColor[bounds.Width * bounds.Height];
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

        return new SerializedArtImage(bounds.Width, bounds.Height, pixels, isStatic);
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

    private static void WriteLandEntry(BinaryWriter writer, SerializedArtImage imageData)
    {
        if (imageData.Width != LandTileSize || imageData.Height != LandTileSize)
        {
            throw new InvalidOperationException("Land art must be saved as a 44x44 image.");
        }

        var xOffset = LandTileSize / 2;
        var runLength = 2;
        for (var y = 0; y < LandTileSize / 2; y++)
        {
            for (var x = 0; x < runLength; x++)
            {
                writer.Write((ushort)(imageData.Pixels[(y * imageData.Width) + xOffset + x] ^ 0x8000));
            }

            xOffset--;
            runLength += 2;
        }

        xOffset = 0;
        runLength = LandTileSize;
        for (var y = LandTileSize / 2; y < LandTileSize; y++)
        {
            for (var x = 0; x < runLength; x++)
            {
                writer.Write((ushort)(imageData.Pixels[(y * imageData.Width) + xOffset + x] ^ 0x8000));
            }

            xOffset++;
            runLength -= 2;
        }
    }

    private static void WriteStaticEntry(BinaryWriter writer, SerializedArtImage imageData)
    {
        writer.Write(1234);
        writer.Write((short)imageData.Width);
        writer.Write((short)imageData.Height);

        var lookupPosition = writer.BaseStream.Position;
        for (var row = 0; row < imageData.Height; row++)
        {
            writer.Write((short)0);
        }

        var pixelDataStart = writer.BaseStream.Position;
        for (var y = 0; y < imageData.Height; y++)
        {
            var rowOffset = checked((int)((writer.BaseStream.Position - pixelDataStart) / 2));
            var returnPosition = writer.BaseStream.Position;
            writer.BaseStream.Seek(lookupPosition + (y * sizeof(short)), SeekOrigin.Begin);
            writer.Write((short)rowOffset);
            writer.BaseStream.Seek(returnPosition, SeekOrigin.Begin);

            var rowStart = y * imageData.Width;
            var x = 0;
            var previousRunEnd = 0;
            while (x < imageData.Width)
            {
                while (x < imageData.Width && imageData.Pixels[rowStart + x] == 0)
                {
                    x++;
                }

                if (x >= imageData.Width)
                {
                    break;
                }

                var runStart = x;
                while (x < imageData.Width && imageData.Pixels[rowStart + x] != 0)
                {
                    x++;
                }

                writer.Write((short)(runStart - previousRunEnd));
                writer.Write((short)(x - runStart));
                for (var pixelIndex = runStart; pixelIndex < x; pixelIndex++)
                {
                    writer.Write((ushort)(imageData.Pixels[rowStart + pixelIndex] ^ 0x8000));
                }

                previousRunEnd = x;
            }

            writer.Write((short)0);
            writer.Write((short)0);
        }
    }

    private void PopulateValidIds()
    {
        if (_uoFileManager == null)
        {
            return;
        }

        _validLandIds.Clear();
        for (var i = 0; i < _uoFileManager.TileData.LandData.Length; i++)
        {
            var isArtValid = _uoFileManager.Arts.File.GetValidRefEntry(i).Length > 0;
            var texId = _uoFileManager.TileData.LandData[i].TexID;
            var isTexValid = _uoFileManager.Texmaps.File.GetValidRefEntry(texId).Length > 0;
            if (isArtValid || isTexValid)
            {
                _validLandIds.Add((ushort)i);
            }
        }

        _validStaticIds.Clear();
        for (var i = 0; i < _uoFileManager.TileData.StaticData.Length; i++)
        {
            if (!_uoFileManager.Arts.File.GetValidRefEntry(i + ArtLoader.MAX_LAND_DATA_INDEX_COUNT).Equals(UOFileIndex.Invalid))
            {
                _validStaticIds.Add((ushort)i);
            }
        }
    }

    private AssetTilePreview GetLandPreview(ushort assetId, bool preferTexmaps)
    {
        if (_uoFileManager == null || _arts == null || _texmaps == null)
        {
            return AssetTilePreview.Invalid;
        }

        if (assetId >= _uoFileManager.TileData.LandData.Length)
        {
            return AssetTilePreview.Invalid;
        }

        var tileData = _uoFileManager.TileData.LandData[assetId];
        var isArtValid = _uoFileManager.Arts.File.GetValidRefEntry(assetId).Length > 0;
        var isTexValid = _uoFileManager.Texmaps.File.GetValidRefEntry(tileData.TexID).Length > 0;
        var useTexmap = isTexValid && (preferTexmaps || !isArtValid);
        var spriteInfo = useTexmap ? _texmaps.GetTexmap(tileData.TexID) : _arts.GetLand(isArtValid ? assetId : 0u);

        if (spriteInfo.Texture == null)
        {
            spriteInfo = _texmaps.GetTexmap(0x0001);
        }

        return new AssetTilePreview(
            assetId,
            assetId,
            spriteInfo.Texture,
            new Rectangle(spriteInfo.UV.X, spriteInfo.UV.Y, spriteInfo.UV.Width, spriteInfo.UV.Height),
            tileData.Name ?? string.Empty,
            tileData.Flags.ToString().Replace(", ", "\n"),
            0,
            useTexmap);
    }

    private AssetTilePreview GetStaticPreview(ushort assetId)
    {
        if (_uoFileManager == null || _arts == null || _texmaps == null)
        {
            return AssetTilePreview.Invalid;
        }

        if (assetId >= _uoFileManager.TileData.StaticData.Length)
        {
            return AssetTilePreview.Invalid;
        }

        ref var indexEntry = ref _uoFileManager.Arts.File.GetValidRefEntry(assetId + 0x4000);
        var realIndex = assetId + indexEntry.AnimOffset;
        var spriteInfo = _arts.GetArt((uint)realIndex);
        if (spriteInfo.Texture == null)
        {
            spriteInfo = _texmaps.GetTexmap(0x0001);
        }

        var realBounds = _arts.GetRealArtBounds((uint)realIndex);
        var tileData = _uoFileManager.TileData.StaticData[assetId];

        return new AssetTilePreview(
            assetId,
            assetId + ArtLoader.MAX_LAND_DATA_INDEX_COUNT,
            spriteInfo.Texture,
            new Rectangle(spriteInfo.UV.X + realBounds.X, spriteInfo.UV.Y + realBounds.Y, realBounds.Width, realBounds.Height),
            tileData.Name ?? string.Empty,
            tileData.Flags.ToString().Replace(", ", "\n"),
            tileData.Height,
            false);
    }

    private AssetTilePreview BuildReplacementPreview(bool objectMode, ushort assetId, StagedReplacement replacement)
    {
        if (_uoFileManager == null)
        {
            return AssetTilePreview.Invalid;
        }

        if (objectMode)
        {
            var tileData = _uoFileManager.TileData.StaticData[assetId];
            return new AssetTilePreview(
                assetId,
                assetId + ArtLoader.MAX_LAND_DATA_INDEX_COUNT,
                replacement.Texture,
                replacement.Bounds,
                tileData.Name ?? string.Empty,
                tileData.Flags.ToString().Replace(", ", "\n"),
                tileData.Height,
                false);
        }

        var landTileData = _uoFileManager.TileData.LandData[assetId];
        return new AssetTilePreview(
            assetId,
            assetId,
            replacement.Texture,
            replacement.Bounds,
            landTileData.Name ?? string.Empty,
            landTileData.Flags.ToString().Replace(", ", "\n"),
            0,
            false);
    }

    private void ResetState()
    {
        foreach (var replacement in _stagedReplacements.Values)
        {
            replacement.Texture.Dispose();
        }

        _stagedReplacements.Clear();
        _stagedRemovals.Clear();
        IsReady = false;
        LoadedRootPath = string.Empty;
        _uoFileManager = null;
        _arts = null;
        _texmaps = null;
        _graphicsDevice = null;
        _validLandIds.Clear();
        _validStaticIds.Clear();
    }
}