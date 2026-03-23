using ClassicUO.Assets;
using ClassicUO.IO;
using ClassicUO.Renderer.Texmaps;
using ClassicUO.Utility;
using Microsoft.Xna.Framework.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FNAColor = Microsoft.Xna.Framework.Color;
using Rectangle = System.Drawing.Rectangle;

namespace CentrED.Assets;

/// <summary>
/// Preview metadata for one locally loaded texture-map entry.
/// </summary>
public readonly record struct AssetTexturePreview(
    ushort TextureId,
    Texture2D? Texture,
    Rectangle Bounds,
    int PixelSize)
{
    /// <summary>
    /// Sentinel returned when a texture preview cannot be produced.
    /// </summary>
    public static AssetTexturePreview Invalid => new(0, null, default, 0);

    /// <summary>
    /// Gets whether the preview contains a valid source texture region.
    /// </summary>
    public bool IsValid => Texture != null && Bounds.Width > 0 && Bounds.Height > 0;
}

/// <summary>
/// Loads local texture-map assets directly from a Ultima client directory for the asset workspace.
/// </summary>
public sealed class AssetTextureBrowserService
{
    private static readonly HashSet<int> ValidTextureSizes = [64, 128];

    private sealed record StagedReplacement(Texture2D Texture, Rectangle Bounds, string SourcePath, int PixelSize);
    private readonly record struct SerializedTextureImage(int PixelSize, ushort[] Pixels);

    private UOFileManager? _uoFileManager;
    private Texmap? _texmaps;
    private GraphicsDevice? _graphicsDevice;
    private readonly List<ushort> _validTextureIds = [];
    private readonly Dictionary<ushort, StagedReplacement> _stagedReplacements = new();
    private readonly HashSet<ushort> _stagedRemovals = [];

    /// <summary>
    /// Gets the root path currently loaded into the browser service.
    /// </summary>
    public string LoadedRootPath { get; private set; } = string.Empty;

    /// <summary>
    /// Gets whether the local texture files are currently loaded and ready.
    /// </summary>
    public bool IsReady { get; private set; }

    /// <summary>
    /// Gets the last service status message.
    /// </summary>
    public string StatusMessage { get; private set; } = "Choose a Ultima data directory to start browsing texture maps.";

    /// <summary>
    /// Gets the valid texture ids discovered in the currently loaded client directory.
    /// </summary>
    public IReadOnlyList<ushort> ValidTextureIds => _validTextureIds;

    /// <summary>
    /// Gets the number of staged local texture changes that have not yet been persisted.
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

        var clientVersion = ResolveClientVersion(rootPath);

        try
        {
            _uoFileManager = new UOFileManager(clientVersion, rootPath);
            _uoFileManager.Texmaps.Load();

            _texmaps = new Texmap(_uoFileManager.Texmaps, graphicsDevice);
            _graphicsDevice = graphicsDevice;

            PopulateValidIds();

            LoadedRootPath = AssetWorkspaceService.NormalizePath(rootPath);
            IsReady = true;
            StatusMessage = $"Loaded {_validTextureIds.Count} texture entries from {LoadedRootPath}.";
        }
        catch (Exception ex)
        {
            ResetState();
            StatusMessage = $"Failed to load local texture assets: {ex.Message}";
        }
    }

    /// <summary>
    /// Filters the available texture ids using numeric text matching.
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
            results.AddRange(_validTextureIds);
            return results;
        }

        foreach (var id in _validTextureIds)
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
    /// Builds a texture preview entry for display in the asset workspace.
    /// </summary>
    public AssetTexturePreview GetPreview(ushort textureId)
    {
        if (!IsReady || _uoFileManager == null || _texmaps == null)
        {
            return AssetTexturePreview.Invalid;
        }

        if (_stagedRemovals.Contains(textureId))
        {
            return AssetTexturePreview.Invalid;
        }

        if (_stagedReplacements.TryGetValue(textureId, out var replacement))
        {
            return new AssetTexturePreview(textureId, replacement.Texture, replacement.Bounds, replacement.PixelSize);
        }

        if (_uoFileManager.Texmaps.File.GetValidRefEntry(textureId).Length <= 0)
        {
            return AssetTexturePreview.Invalid;
        }

        var spriteInfo = _texmaps.GetTexmap(textureId);
        if (spriteInfo.Texture == null || spriteInfo.UV.Width <= 0 || spriteInfo.UV.Height <= 0)
        {
            return AssetTexturePreview.Invalid;
        }

        return new AssetTexturePreview(
            textureId,
            spriteInfo.Texture,
            new Rectangle(spriteInfo.UV.X, spriteInfo.UV.Y, spriteInfo.UV.Width, spriteInfo.UV.Height),
            spriteInfo.UV.Width);
    }

    /// <summary>
    /// Gets whether one texture currently has a staged change.
    /// </summary>
    public bool IsDirty(ushort textureId)
    {
        return _stagedReplacements.ContainsKey(textureId) || _stagedRemovals.Contains(textureId);
    }

    /// <summary>
    /// Gets whether one texture is staged for removal on the next save.
    /// </summary>
    public bool IsMarkedForRemoval(ushort textureId)
    {
        return _stagedRemovals.Contains(textureId);
    }

    /// <summary>
    /// Returns the source path for a staged replacement when present.
    /// </summary>
    public string GetReplacementSourcePath(ushort textureId)
    {
        return _stagedReplacements.TryGetValue(textureId, out var replacement)
            ? replacement.SourcePath
            : string.Empty;
    }

    /// <summary>
    /// Exports the currently visible preview for one texture to an image file.
    /// </summary>
    public void ExportPreview(ushort textureId, string outputPath)
    {
        var preview = GetPreview(textureId);
        if (!preview.IsValid || preview.Texture == null)
        {
            throw new InvalidOperationException("No preview is available for the selected texture.");
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
    /// Loads a replacement image and stages it as an in-memory override for one texture preview.
    /// </summary>
    public void StageReplacement(ushort textureId, string imagePath)
    {
        if (_graphicsDevice == null)
        {
            throw new InvalidOperationException("Texture graphics device is not ready.");
        }

        using var image = Image.Load<Rgba32>(imagePath);
        if (image.Width != image.Height || !ValidTextureSizes.Contains(image.Width))
        {
            throw new InvalidOperationException("Texture replacements must be square 64x64 or 128x128 images.");
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

        if (_stagedReplacements.Remove(textureId, out var existing))
        {
            existing.Texture.Dispose();
        }

        _stagedRemovals.Remove(textureId);
        _stagedReplacements[textureId] = new StagedReplacement(texture, new Rectangle(0, 0, image.Width, image.Height), imagePath, image.Width);

        if (!_validTextureIds.Contains(textureId))
        {
            _validTextureIds.Add(textureId);
            _validTextureIds.Sort();
        }
    }

    /// <summary>
    /// Stages one texture entry for removal from texmaps.mul on the next save.
    /// </summary>
    public void StageRemoval(ushort textureId)
    {
        if (_stagedReplacements.Remove(textureId, out var replacement))
        {
            replacement.Texture.Dispose();
        }

        _stagedRemovals.Add(textureId);
    }

    /// <summary>
    /// Clears any staged replacement or removal for one texture.
    /// </summary>
    public void ClearStagedChange(ushort textureId)
    {
        if (_stagedReplacements.Remove(textureId, out var replacement))
        {
            replacement.Texture.Dispose();
        }

        _stagedRemovals.Remove(textureId);
    }

    /// <summary>
    /// Saves the current staged texture changes into texmaps.mul and texidx.mul.
    /// Existing files are copied to .bak before the new archives are written.
    /// </summary>
    public void SaveStagedChanges()
    {
        if (!IsReady || _uoFileManager == null || _texmaps == null || _graphicsDevice == null)
        {
            throw new InvalidOperationException("Local texture assets are not loaded.");
        }

        if (DirtyCount == 0)
        {
            throw new InvalidOperationException("There are no staged texture changes to save.");
        }

        var rootPath = LoadedRootPath;
        if (rootPath.Length == 0 || !Directory.Exists(rootPath))
        {
            throw new InvalidOperationException("The selected Ultima data directory is no longer available.");
        }

        var texIdxPath = Path.Combine(rootPath, "texidx.mul");
        var texMulPath = Path.Combine(rootPath, "texmaps.mul");
        var tempIdxPath = texIdxPath + ".tmp";
        var tempMulPath = texMulPath + ".tmp";
        var stagedCount = DirtyCount;

        try
        {
            using (var idxStream = new FileStream(tempIdxPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var mulStream = new FileStream(tempMulPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var idxWriter = new BinaryWriter(idxStream))
            using (var mulWriter = new BinaryWriter(mulStream))
            {
                var savedChecksums = new Dictionary<string, (int Position, int Length, int Extra)>(StringComparer.Ordinal);

                for (var index = 0; index < 0x4000; index++)
                {
                    if (!TryGetImageForSave((ushort)index, out var imageData))
                    {
                        idxWriter.Write(0);
                        idxWriter.Write(0);
                        idxWriter.Write(0);
                        continue;
                    }

                    var checksum = BuildChecksumKey(imageData);
                    if (savedChecksums.TryGetValue(checksum, out var savedEntry))
                    {
                        idxWriter.Write(savedEntry.Position);
                        idxWriter.Write(savedEntry.Length);
                        idxWriter.Write(savedEntry.Extra);
                        continue;
                    }

                    var entryStart = checked((int)mulWriter.BaseStream.Position);
                    WriteTextureEntry(mulWriter, imageData);
                    var entryLength = checked((int)mulWriter.BaseStream.Position) - entryStart;
                    var extra = imageData.PixelSize == 128 ? 1 : 0;

                    idxWriter.Write(entryStart);
                    idxWriter.Write(entryLength);
                    idxWriter.Write(extra);

                    savedChecksums[checksum] = (entryStart, entryLength, extra);
                }
            }

            BackupIfPresent(texIdxPath);
            BackupIfPresent(texMulPath);
            File.Move(tempIdxPath, texIdxPath, true);
            File.Move(tempMulPath, texMulPath, true);
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
        StatusMessage = $"Saved {stagedCount} staged texture change(s) to {texMulPath}.";
    }

    private static ClientVersion ResolveClientVersion(string rootPath)
    {
        var tiledataPath = Path.Combine(rootPath, "tiledata.mul");
        if (!File.Exists(tiledataPath))
        {
            return ClientVersion.CV_6000;
        }

        return new FileInfo(tiledataPath).Length switch
        {
            >= 3188736 => ClientVersion.CV_7090,
            >= 1644544 => ClientVersion.CV_7000,
            _ => ClientVersion.CV_6000,
        };
    }

    private void PopulateValidIds()
    {
        if (_uoFileManager == null)
        {
            return;
        }

        _validTextureIds.Clear();
        for (var i = 0; i < 0x4000; i++)
        {
            if (_uoFileManager.Texmaps.File.GetValidRefEntry(i).Length > 0)
            {
                _validTextureIds.Add((ushort)i);
            }
        }
    }

    private bool TryGetImageForSave(ushort textureId, out SerializedTextureImage imageData)
    {
        imageData = default;

        if (_stagedRemovals.Contains(textureId))
        {
            return false;
        }

        if (_stagedReplacements.TryGetValue(textureId, out var replacement))
        {
            imageData = BuildSerializedImage(replacement.Texture, replacement.Bounds, replacement.PixelSize);
            return true;
        }

        return TryGetOriginalTextureImage(textureId, out imageData);
    }

    private bool TryGetOriginalTextureImage(ushort textureId, out SerializedTextureImage imageData)
    {
        imageData = default;
        if (_uoFileManager == null || _texmaps == null)
        {
            return false;
        }

        if (_uoFileManager.Texmaps.File.GetValidRefEntry(textureId).Length <= 0)
        {
            return false;
        }

        var spriteInfo = _texmaps.GetTexmap(textureId);
        if (spriteInfo.Texture == null || !ValidTextureSizes.Contains(spriteInfo.UV.Width) || spriteInfo.UV.Width != spriteInfo.UV.Height)
        {
            return false;
        }

        imageData = BuildSerializedImage(
            spriteInfo.Texture,
            new Rectangle(spriteInfo.UV.X, spriteInfo.UV.Y, spriteInfo.UV.Width, spriteInfo.UV.Height),
            spriteInfo.UV.Width);
        return true;
    }

    private static SerializedTextureImage BuildSerializedImage(Texture2D texture, Rectangle bounds, int pixelSize)
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

        return new SerializedTextureImage(pixelSize, pixels);
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

    private static string BuildChecksumKey(SerializedTextureImage imageData)
    {
        return $"{imageData.PixelSize}:{Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(imageData.Pixels.AsSpan())))}";
    }

    private static void WriteTextureEntry(BinaryWriter writer, SerializedTextureImage imageData)
    {
        for (var i = 0; i < imageData.Pixels.Length; i++)
        {
            writer.Write((ushort)(imageData.Pixels[i] ^ 0x8000));
        }
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

        _stagedReplacements.Clear();
        _stagedRemovals.Clear();
        _validTextureIds.Clear();
        _uoFileManager = null;
        _texmaps = null;
        _graphicsDevice = null;
        LoadedRootPath = string.Empty;
        IsReady = false;
    }
}