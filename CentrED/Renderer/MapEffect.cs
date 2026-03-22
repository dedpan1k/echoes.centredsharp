using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CentrED.Renderer.Effects;

/// <summary>
/// Wraps the map shader and exposes strongly typed parameter setters.
/// </summary>
public class MapEffect : Effect
{
    /// <summary>
    /// Sets the combined world-view-projection matrix.
    /// </summary>
    public Matrix WorldViewProj
    {
        set => Parameters["WorldViewProj"].SetValue(value);
    }

    /// <summary>
    /// Sets the fill color used for the virtual layer.
    /// </summary>
    public Vector4 VirtualLayerFillColor
    {
        set => Parameters["VirtualLayerFillColor"].SetValue(value);
    }

    /// <summary>
    /// Sets the border color used for the virtual layer.
    /// </summary>
    public Vector4 VirtualLayerBorderColor
    {
        set => Parameters["VirtualLayerBorderColor"].SetValue(value);
    }
    
    /// <summary>
    /// Sets the flat-terrain grid color.
    /// </summary>
    public Vector4 TerrainGridFlatColor
    {
        set => Parameters["TerrainGridFlatColor"].SetValue(value);
    }
    
    /// <summary>
    /// Sets the angled-terrain grid color.
    /// </summary>
    public Vector4 TerrainGridAngledColor
    {
        set => Parameters["TerrainGridAngledColor"].SetValue(value);
    }

    /// <summary>
    /// Reads an embedded shader resource into memory.
    /// </summary>
    /// <param name="name">The manifest resource name.</param>
    /// <returns>The resource bytes, or an empty array when missing.</returns>
    protected static byte[] GetResource(string name)
    {
        Stream? stream = typeof(MapEffect).Assembly.GetManifestResourceStream(name);

        if (stream == null)
        {
            return Array.Empty<byte>();
        }

        using (MemoryStream ms = new MemoryStream())
        {
            stream.CopyTo(ms);

            return ms.ToArray();
        }
    }

    /// <summary>
    /// Initializes the effect from the embedded compiled shader.
    /// </summary>
    /// <param name="device">The graphics device.</param>
    public MapEffect(GraphicsDevice device) : this
        (device, GetResource("CentrED.Renderer.Shaders.MapEffect.fxc"))
    {
    }
    
    /// <summary>
    /// Initializes the effect from compiled shader bytecode.
    /// </summary>
    /// <param name="device">The graphics device.</param>
    /// <param name="effectCode">The compiled shader bytecode.</param>
    public MapEffect(GraphicsDevice device, byte[] effectCode) : base(device, effectCode)
    {
        Parameters["VirtualLayerFillColor"].SetValue(new Vector4(0.2f, 0.2f, 0.2f, 0.1f));
        Parameters["VirtualLayerBorderColor"].SetValue(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        Parameters["TerrainGridFlatColor"].SetValue(new Vector4(0.5f, 0.5f, 0.0f, 0.5f));
        Parameters["TerrainGridAngledColor"].SetValue(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
    }
}