using System.Numerics;
using CentrED.Renderer;
using Microsoft.Xna.Framework.Graphics;
using static CentrED.Constants;

namespace CentrED.Map;

/// <summary>
/// Represents an editor image overlay anchored in world space above the map.
/// </summary>
public class ImageOverlay : MapObject
{
    /// <summary>
    /// Initializes an empty image overlay.
    /// </summary>
    public ImageOverlay()
    {
        for (int i = 0; i < 4; i++)
        {
            Vertices[i] = new MapVertex(Vector3.Zero, Vector3.Zero, Vector4.Zero, Vector3.Zero);
        }
    }

    private bool _enabled;
    private bool _drawAboveTerrain;
    private int _worldX;
    private int _worldY;
    private float _scale = 1.0f;
    private float _opacity = 1.0f;
    private float _screen = 0.0f;

    /// <summary>
    /// Gets or sets a value indicating whether the overlay is enabled.
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the overlay should render above terrain.
    /// </summary>
    public bool DrawAboveTerrain
    {
        get => _drawAboveTerrain;
        set => _drawAboveTerrain = value;
    }

    /// <summary>
    /// Gets or sets the world X coordinate in tiles.
    /// </summary>
    public int WorldX
    {
        get => _worldX;
        set
        {
            _worldX = value;
            UpdateVertices();
        }
    }

    /// <summary>
    /// Gets or sets the world Y coordinate in tiles.
    /// </summary>
    public int WorldY
    {
        get => _worldY;
        set
        {
            _worldY = value;
            UpdateVertices();
        }
    }

    /// <summary>
    /// Gets or sets the overlay scale factor.
    /// </summary>
    public float Scale
    {
        get => _scale;
        set
        {
            _scale = Math.Max(0.1f, Math.Min(10.0f, value));
            UpdateVertices();
        }
    }

    /// <summary>
    /// Gets or sets the overlay opacity from 0 to 1.
    /// </summary>
    public float Opacity
    {
        get => _opacity;
        set
        {
            _opacity = Math.Max(0.0f, Math.Min(1.0f, value));
            for (int i = 0; i < 4; i++)
            {
                Vertices[i].Hue.Z = _opacity;
            }
        }
    }

    /// <summary>
    /// Gets or sets the screen-space blending factor from 0 to 1.
    /// </summary>
    public float Screen
    {
        get => _screen;
        set
        {
            _screen = Math.Max(0.0f, Math.Min(1.0f, value));
            for (int i = 0; i < 4; i++)
            {
                Vertices[i].Hue.Y = _screen;
            }
        }
    }

    /// <summary>
    /// Gets the loaded image width in pixels.
    /// </summary>
    public int ImageWidth => Texture?.Width ?? 0;

    /// <summary>
    /// Gets the loaded image height in pixels.
    /// </summary>
    public int ImageHeight => Texture?.Height ?? 0;

    /// <summary>
    /// Gets the scaled overlay width expressed in tiles.
    /// </summary>
    public float WidthInTiles => ImageWidth * _scale;

    /// <summary>
    /// Gets the scaled overlay height expressed in tiles.
    /// </summary>
    public float HeightInTiles => ImageHeight * _scale;

    /// <summary>
    /// Loads an overlay image from disk.
    /// </summary>
    /// <param name="gd">The graphics device used to create the texture.</param>
    /// <param name="path">The image path.</param>
    public void LoadImage(GraphicsDevice gd, string path)
    {
        UnloadImage();

        using var fileStream = File.OpenRead(path);
        Texture = Texture2D.FromStream(gd, fileStream);
        TextureBounds = new System.Drawing.Rectangle(0, 0, Texture.Width, Texture.Height);
        UpdateVertices();
    }

    /// <summary>
    /// Unloads the current overlay texture.
    /// </summary>
    public void UnloadImage()
    {
        if (Texture != null)
        {
            Texture.Dispose();
            Texture = null!;
        }
    }

    /// <summary>
    /// Rebuilds the overlay quad after a transform or texture change.
    /// </summary>
    private void UpdateVertices()
    {
        if (Texture == null)
            return;

        float width = WidthInTiles * TILE_SIZE;
        float height = HeightInTiles * TILE_SIZE;
        float x = _worldX * TILE_SIZE;
        float y = _worldY * TILE_SIZE;

        Vertices[0].Position = new Vector3(x, y, 0);
        Vertices[0].Texture = new Vector3(0, 0, 0);
        Vertices[1].Position = new Vector3(x + width, y, 0);
        Vertices[1].Texture = new Vector3(1, 0, 0);
        Vertices[2].Position = new Vector3(x, y + height, 0);
        Vertices[2].Texture = new Vector3(0, 1, 0);
        Vertices[3].Position = new Vector3(x + width, y + height, 0);
        Vertices[3].Texture = new Vector3(1, 1, 0);

        for (int i = 0; i < 4; i++)
        {
            Vertices[i].Hue = new Vector4(0, _screen, _opacity, 0);
            Vertices[i].Normal = Vector3.Zero;
        }
    }
}
