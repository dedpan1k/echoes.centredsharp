using System.Drawing;
using System.Numerics;
using CentrED.Renderer;
using Microsoft.Xna.Framework.Graphics;

namespace CentrED.Map;

/// <summary>
/// Represents a renderable object on the editor map, including picking metadata and quad geometry.
/// </summary>
public abstract class MapObject
{
    private static int NextObjectId = 1;

    /// <summary>
    /// Initializes a new map object and assigns a unique picking identifier.
    /// </summary>
    public MapObject()
    {
        ObjectId = GetNextId();
        ObjectIdColor = new Vector4((ObjectId & 0xFF) / 255f, ((ObjectId >> 8) & 0xFF) / 255f, ((ObjectId >> 16) & 0xFF) / 255f, 1.0f);
    }

    /// <summary>
    /// Generates the next unique object identifier used for color-based picking.
    /// </summary>
    /// <returns>The next positive object identifier.</returns>
    public static int GetNextId()
    {
        var objectId = NextObjectId++;
        // Reset the picking id space before overflow corrupts color-based selection.
        if (NextObjectId < 0)
        {
            NextObjectId = 1;
            Application.CEDGame.MapManager.Reset();
        }
        return objectId;
    }

    /// <summary>
    /// Gets the unique identifier used to distinguish this object during picking.
    /// </summary>
    public readonly int ObjectId;

    /// <summary>
    /// Gets the encoded picking color derived from <see cref="ObjectId"/>.
    /// </summary>
    public readonly Vector4 ObjectIdColor;

    /// <summary>
    /// Gets or sets a value indicating whether the object still represents valid map state.
    /// </summary>
    public bool Valid = true;

    /// <summary>
    /// Gets or sets a value indicating whether the object is currently visible.
    /// </summary>
    public bool Visible = true;

    /// <summary>
    /// Gets a value indicating whether the object should be submitted for drawing.
    /// </summary>
    public bool CanDraw => Valid && Visible;

    /// <summary>
    /// Gets or sets the texture used to draw the object.
    /// </summary>
    public Texture2D Texture;

    /// <summary>
    /// Gets or sets the texture-space bounds inside <see cref="Texture"/>.
    /// </summary>
    public Rectangle TextureBounds;

    /// <summary>
    /// Gets the vertex quad used by the map renderer.
    /// </summary>
    public MapVertex[] Vertices = new MapVertex[4];
}