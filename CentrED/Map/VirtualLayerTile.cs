using System.Numerics;
using CentrED.Renderer;

namespace CentrED.Map;

/// <summary>
/// Represents one synthetic tile used to address the virtual editing layer.
/// </summary>
public class VirtualLayerTile : TileObject
{
    private readonly int _hash;

    /// <summary>
    /// Initializes a virtual-layer tile at the supplied coordinates.
    /// </summary>
    /// <param name="x">The tile X coordinate.</param>
    /// <param name="y">The tile Y coordinate.</param>
    /// <param name="z">The preview layer altitude.</param>
    public VirtualLayerTile(ushort x = 0, ushort y = 0, sbyte z = 0)
    {
        _hash = HashCode.Combine(x, y, z);
        Tile = new LandTile(0, x, y, z);
        for (int i = 0; i < 4; i++)
        {
            Vertices[i] = new MapVertex(Vector3.Zero,Vector3.Zero, Vector4.Zero, Vector3.Zero);
        }
    }

    /// <summary>
    /// Compares two virtual-layer tiles by their cached coordinate hash.
    /// </summary>
    /// <param name="other">The other virtual-layer tile.</param>
    /// <returns><see langword="true"/> when both tiles represent the same coordinates.</returns>
    protected bool Equals(VirtualLayerTile other)
    {
        return _hash == other._hash;
    }

    /// <summary>
    /// Determines whether another object represents the same virtual-layer coordinates.
    /// </summary>
    /// <param name="obj">The object to compare.</param>
    /// <returns><see langword="true"/> when the objects are equivalent.</returns>
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }
        if (ReferenceEquals(this, obj))
        {
            return true;
        }
        if (obj.GetType() != this.GetType())
        {
            return false;
        }
        return Equals((VirtualLayerTile)obj);
    }

    /// <summary>
    /// Returns the cached coordinate hash.
    /// </summary>
    /// <returns>The hash code used for dictionary lookups.</returns>
    public override int GetHashCode()
    {
        return _hash;
    }
}