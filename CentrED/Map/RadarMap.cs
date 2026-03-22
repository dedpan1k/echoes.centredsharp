using CentrED.Client;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static CentrED.Application;

namespace CentrED.Map;

/// <summary>
/// Owns the client-side radar texture and applies full-image or incremental updates from the server.
/// </summary>
public class RadarMap
{
    private static RadarMap _instance;

    /// <summary>
    /// Gets the shared radar-map instance.
    /// </summary>
    public static RadarMap Instance => _instance;

    private Texture2D _texture = null!;

    /// <summary>
    /// Gets the current radar texture.
    /// </summary>
    public Texture2D Texture => _texture;

    /// <summary>
    /// Initializes radar-map event handlers for the supplied graphics device.
    /// </summary>
    /// <param name="gd">The graphics device used to create the radar texture.</param>
    private RadarMap(GraphicsDevice gd)
    {
        CEDClient.Connected += () =>
        {
            _texture = new Texture2D(gd, CEDClient.Width, CEDClient.Height);
            CEDClient.Send(new RequestRadarMapPacket());
        };

        CEDClient.RadarData += RadarData;
        CEDClient.RadarUpdate += RadarUpdate;
    }

    /// <summary>
    /// Creates the shared radar-map instance.
    /// </summary>
    /// <param name="gd">The graphics device used to create the radar texture.</param>
    public static void Initialize(GraphicsDevice gd)
    {
        _instance = new RadarMap(gd);
    }

    /// <summary>
    /// Replaces the full radar texture with server-provided color data.
    /// </summary>
    /// <param name="data">The full radar payload in server block order.</param>
    private unsafe void RadarData(ReadOnlySpan<ushort> data)
    {
        var width = CEDClient.Width;
        var height = CEDClient.Height;
        uint[] buffer = System.Buffers.ArrayPool<uint>.Shared.Rent(data.Length);
        for (ushort x = 0; x < width; x++)
        {
            for (ushort y = 0; y < height; y++)
            {
                buffer[y * width + x] = HuesHelper.Color16To32(data[x * height + y]) | 0xFF_00_00_00;
            }
        }

        fixed (uint* ptr = buffer)
        {
            _texture.SetDataPointerEXT(0, null, (IntPtr)ptr, data.Length * sizeof(uint));
        }
    }

    /// <summary>
    /// Applies a single-pixel radar update.
    /// </summary>
    /// <param name="x">The radar X coordinate.</param>
    /// <param name="y">The radar Y coordinate.</param>
    /// <param name="color">The new 16-bit radar color.</param>
    private void RadarUpdate(ushort x, ushort y, ushort color)
    {
        _texture.SetData(0, new Rectangle(x, y, 1, 1), new[] { HuesHelper.Color16To32(color) | 0xFF_00_00_00 }, 0, 1);
    }
}