using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;

namespace CentrED;

/// <summary>
/// Loads hue data and exposes shader-friendly lookup values for tinted rendering.
/// </summary>
public class HuesManager
{
    private static HuesManager _instance;

    /// <summary>
    /// Gets the active hue manager instance.
    /// </summary>
    public static HuesManager Instance => _instance;

    /// <summary>
    /// The number of color entries contained in a single hue table.
    /// </summary>
    public const int HUE_SIZE = 32;
    // Pack 16 hue tables per row; each table contributes 32 color entries.

    /// <summary>
    /// The width, in pixels, of the generated hue lookup texture.
    /// </summary>
    public const int TEXTURE_WIDTH = 16 * HUE_SIZE;

    // Size the texture large enough to hold the complete shader lookup table
    // without reallocating based on the data-file contents.

    /// <summary>
    /// The height, in pixels, of the generated hue lookup texture.
    /// </summary>
    public const int TEXTURE_HEIGHT = 1024;

    /// <summary>
    /// Gets the GPU texture that stores hue lookup colors for shaders.
    /// </summary>
    public readonly Texture2D Texture;

    /// <summary>
    /// Gets the number of loaded hues, including the reserved no-hue entry.
    /// </summary>
    public readonly int HuesCount;

    /// <summary>
    /// Gets the display names for loaded hues indexed by shader hue id.
    /// </summary>
    public readonly string[] Names;
    // public readonly ushort[][] Colors;

    /// <summary>
    /// The default alpha used for translucent statics.
    /// </summary>
    // Ultima translucency uses roughly 70% opacity for flagged statics.
    public const float TRANSLUCENT_ALPHA = 178 / 255.0f;

    private unsafe HuesManager(GraphicsDevice gd)
    {
        var huesLoader = Application.CEDGame.MapManager.UoFileManager.Hues;

        // Reserve index 0 for "no hue" so shader code can treat zero as the
        // identity case and actual hue data remains 1-based like the client.
        HuesCount = huesLoader.HuesCount + 1;
        Texture = new Texture2D(gd, TEXTURE_WIDTH, TEXTURE_HEIGHT);

        // Build the hue lookup texture in managed memory, upload it once, then
        // return the backing array to the pool to avoid long-lived allocations.
        uint[] buffer = System.Buffers.ArrayPool<uint>.Shared.Rent(TEXTURE_WIDTH * TEXTURE_HEIGHT);

        fixed (uint* ptr = buffer)
        {
            huesLoader.CreateShaderColors(buffer);
            Texture.SetDataPointerEXT(0, null, (IntPtr)ptr, TEXTURE_WIDTH * TEXTURE_HEIGHT * sizeof(uint));
        }

        System.Buffers.ArrayPool<uint>.Shared.Return(buffer);

        // Colors = new ushort[HuesCount + 1][];
        Names = new string[HuesCount + 1];
        // Colors[0] = huesLoader.HuesRange[0].Entries[0].ColorTable;

        // Index 0 mirrors the hue-vector convention used elsewhere: zero means
        // "leave the source color unchanged".
        Names[0] = "No Hue";
        var i = 1;
        foreach (var huesGroup in huesLoader.HuesRange)
        {
            foreach (var hueEntry in huesGroup.Entries)
            {
                // Colors[i] = hueEntry.ColorTable;
                // Hue names are stored as UTF-8 byte buffers in the original
                // asset format, so marshal them into managed strings once here.
                Names[i] = Marshal.PtrToStringUTF8((IntPtr)hueEntry.Name) ?? "Read Error";
                i++;
            }
        }
    }

    /// <summary>
    /// Creates or recreates the hue manager for the provided graphics device.
    /// </summary>
    /// <param name="gd">The graphics device that owns the hue lookup texture.</param>
    public static void Load(GraphicsDevice gd)
    {
        // Rebuild the singleton when the graphics device is ready because the
        // hue texture is a GPU resource owned by that device.
        _instance = new HuesManager(gd);
    }

    /// <summary>
    /// Describes how the shader should interpret hue data for a draw call.
    /// </summary>
    public enum HueMode
    {
        /// <summary>
        /// Leaves the source color unchanged.
        /// </summary>
        NONE = 0,

        /// <summary>
        /// Applies a full hue replacement.
        /// </summary>
        HUED = 1,

        /// <summary>
        /// Applies hue only to pixels that qualify for partial tinting.
        /// </summary>
        PARTIAL = 2,

        /// <summary>
        /// Applies light-based tinting behavior.
        /// </summary>
        LIGHT = 3,

        /// <summary>
        /// Uses the provided RGB color directly instead of the hue lookup texture.
        /// </summary>
        RGB = 255
    }

    /// <summary>
    /// Gets the default alpha value for the specified static tile.
    /// </summary>
    /// <param name="tileId">The static tile identifier.</param>
    /// <returns>The alpha value that should be used when rendering the tile.</returns>
    public float GetDefaultAlpha(ushort tileId)
    {
        // Static tile data carries the translucency flag, which the shader uses
        // to soften foliage, liquids, and similar art automatically.
        return Application.CEDGame.MapManager.UoFileManager.TileData.StaticData[tileId].IsTranslucent ? TRANSLUCENT_ALPHA : 1.0f;
    }

    /// <summary>
    /// Builds a shader hue vector for the specified tile and hue value.
    /// </summary>
    /// <param name="id">The static tile identifier.</param>
    /// <param name="hue">The requested hue value.</param>
    /// <returns>A shader-ready vector describing hue index, alpha, and mode.</returns>
    public Vector4 GetHueVector(ushort id, ushort hue)
    {
        // Some art only hues grayscale pixels. Pull the partial-hue flag from
        // tile data so callers only need to supply the requested hue id.
        var partial = Application.CEDGame.MapManager.UoFileManager.TileData.StaticData[id].IsPartialHue;
        return GetHueVector(hue, partial, GetDefaultAlpha(id));
    }

    private Vector4 GetHueVector(ushort hue, bool partial, float alpha = 1)
    {
        HueMode mode;

        if ((hue & 0x8000) != 0)
        {
            // The high bit forces partial-hue behavior in legacy asset data.
            partial = true;
            hue &= 0x7FFF;
        }

        if (hue != 0)
        {
            // Convert from the file format's 1-based hue ids into the zero-based
            // texture row/column indexing expected by the shader.
            hue -= 1;
            mode = partial ? HueMode.PARTIAL : HueMode.HUED;
        }
        else
        {
            // Zero means "no hue", so the shader should leave the source color
            // unchanged and only honor the supplied alpha.
            mode = HueMode.NONE;
        }

        // The shader interprets this vector as [hueIndex, unused, alpha, mode].
        return new Vector4(hue, 0, alpha, (int)mode);
    }

    /// <summary>
    /// Builds a shader vector that applies a literal RGB tint.
    /// </summary>
    /// <param name="color">The RGB color to apply.</param>
    /// <returns>A shader-ready vector in RGB mode.</returns>
    public Vector4 GetRGBVector(Color color)
    {
        // RGB mode bypasses the hue lookup texture and injects a literal tint.
        return new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, (int)HueMode.RGB);
    }
}