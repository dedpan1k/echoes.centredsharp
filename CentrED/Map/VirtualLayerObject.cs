using System.Numerics;
using CentrED.Renderer;
using static CentrED.Constants;

namespace CentrED.Map;

/// <summary>
/// Represents the translucent helper plane used for virtual-layer previews.
/// </summary>
public class VirtualLayerObject : MapObject
{
    private VirtualLayerObject()
    {
        for (int i = 0; i < 4; i++)
        {
            Vertices[i] = new MapVertex(Vector3.Zero,Vector3.Zero, Vector4.Zero, Vector3.Zero);
        }
    }
    
    private static VirtualLayerObject _instance = new();

    /// <summary>
    /// Gets the shared virtual-layer overlay instance.
    /// </summary>
    public static VirtualLayerObject Instance => _instance;

    private ushort _width;
    private ushort _height;
    private sbyte _z;
    
    /// <summary>
    /// Gets or sets the overlay width in tiles.
    /// </summary>
    public ushort Width
    {
        get => _width;
        set
        {
            _width = value;
            Vertices[1].Position.X = _width * TILE_SIZE;
            Vertices[3].Position.X = _width * TILE_SIZE;
        }
    }

    /// <summary>
    /// Gets or sets the overlay height in tiles.
    /// </summary>
    public ushort Height
    {
        get => _height;
        set
        {
            _height = value; 
            Vertices[2].Position.Y = _height * TILE_SIZE;
            Vertices[3].Position.Y = _height * TILE_SIZE;
        }
    }

    /// <summary>
    /// Gets or sets the virtual-layer altitude.
    /// </summary>
    public sbyte Z
    {
        get => _z;
        set
        {
            _z = value;
            for (var i = 0; i < Vertices.Length; i++)
            {
                Vertices[i].Position.Z = _z * TILE_Z_SCALE;
            }
        }
    }

    /// <summary>
    /// Sets the tint color for the virtual-layer overlay.
    /// </summary>
    public Vector4 Color
    {
        set
        {
            for (var i = 0; i < Vertices.Length; i++)
            {
                Vertices[i].Hue = value;
            }
        }
    }

    /// <summary>
    /// Sets the overlay alpha used by the renderer for this helper plane.
    /// </summary>
    public float Alpha
    {
        set
        {
            for (var i = 0; i < Vertices.Length; i++)
            {
                Vertices[i].Texture.X = value;
            }
        }
    }
}