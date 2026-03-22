using System.Drawing;
using System.Numerics;
using CentrED.Renderer;
using CentrED.UI;
using ClassicUO.Renderer;
using static CentrED.Application;
using static CentrED.Constants;

namespace CentrED.Map;

/// <summary>
/// Wraps a static tile with render geometry, hue state, and ghost/highlight behavior.
/// </summary>
public class StaticObject : TileObject, IComparable<StaticObject>
{
    /// <summary>
    /// Gets the strongly typed static tile represented by this object.
    /// </summary>
    public StaticTile StaticTile;

    /// <summary>
    /// Gets a value indicating whether the static uses animated art.
    /// </summary>
    public bool IsAnimated;

    /// <summary>
    /// Gets a value indicating whether the static acts as a light source.
    /// </summary>
    public bool IsLight;

    /// <summary>
    /// Gets the real art bounds used for hit testing and lighting behavior.
    /// </summary>
    public Rectangle RealBounds;

    /// <summary>
    /// Initializes a renderable static object for the supplied tile.
    /// </summary>
    /// <param name="tile">The static tile to wrap.</param>
    public StaticObject(StaticTile tile)
    {
        // Statics render as two quads so the isometric billboard keeps the expected silhouette.
        Vertices = new MapVertex[8];
        Tile = StaticTile = tile;
        
        var realBounds = CEDGame.MapManager.Arts.GetRealArtBounds(Tile.Id);
        RealBounds = new Rectangle(realBounds.X, realBounds.Y, realBounds.Width, realBounds.Height);
        UpdateId(Tile.Id);
        UpdatePos(tile.X, tile.Y, tile.Z);
        UpdateHue(tile.Hue);
        for (int i = 0; i < Vertices.Length; i++)
        {
            Vertices[i].Normal = Vector3.Zero;
        }
        var tiledata = CEDGame.MapManager.UoFileManager.TileData.StaticData[Tile.Id];
        IsAnimated = tiledata.IsAnimated;
        IsLight = tiledata.IsLight;
    }

    /// <summary>
    /// Refreshes the rendered position from the current tile state.
    /// </summary>
    public void Update()
    {
        //Only UpdatePos for now, mainly for FlatView
        UpdatePos(Tile.X, Tile.Y, Tile.Z);
    }

    /// <summary>
    /// Refreshes the rendered art for the current tile identifier.
    /// </summary>
    public void UpdateId()
    {
        UpdateId(Tile.Id);
    }
    
    /// <summary>
    /// Updates the rendered art and UV layout for a new static identifier.
    /// </summary>
    /// <param name="newId">The static tile identifier to render.</param>
    public void UpdateId(ushort newId)
    {
        var mapManager = CEDGame.MapManager;
        ref var index = ref mapManager.UoFileManager.Arts.File.GetValidRefEntry(newId + 0x4000);
        var spriteInfo = mapManager.Arts.GetArt((uint)(newId + index.AnimOffset));
        if (spriteInfo.Equals(SpriteInfo.Empty))
        {
            if(mapManager.DebugLogging)
                Console.WriteLine($"No texture found for static {Tile.X},{Tile.Y},{Tile.Z}:{newId.FormatId()}");
            //VOID texture of land is by default all pink, so it should be noticeable that something is not right
            spriteInfo = CEDGame.MapManager.Texmaps.GetTexmap(0x0001);
        }
        
        Texture = spriteInfo.Texture;
        var bounds = spriteInfo.UV;
        TextureBounds = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        
        var texX = TextureBounds.X / (float)Texture.Width;
        var texY = TextureBounds.Y / (float)Texture.Height;
        var texWidth = TextureBounds.Width / (float)Texture.Width;
        var halfTexWidth = texWidth * 0.5f;
        var texHeight = TextureBounds.Height / (float)Texture.Height;

        //Left half
        Vertices[0].Texture = new Vector3(texX, texY, 0f);
        Vertices[1].Texture = new Vector3(texX + halfTexWidth, texY, 0f);
        Vertices[2].Texture = new Vector3(texX, texY + texHeight, 0f);
        Vertices[3].Texture = new Vector3(texX + halfTexWidth, texY + texHeight, 0f);
        
        //Right half
        Vertices[4].Texture = new Vector3(texX + halfTexWidth, texY, 0f);
        Vertices[5].Texture = new Vector3(texX + texWidth, texY, 0f);
        Vertices[6].Texture = new Vector3(texX + halfTexWidth, texY + texHeight, 0f);
        Vertices[7].Texture = new Vector3(texX + texWidth, texY + texHeight, 0f);
        UpdateDepthOffset();
        IsAnimated = mapManager.UoFileManager.TileData.StaticData[newId].IsAnimated;
    }

    /// <summary>
    /// Updates the depth bias encoded into each vertex.
    /// </summary>
    public void UpdateDepthOffset()
    {
        var depthOffset = StaticTile.CellIndex * 0.00001f;
        for (int i = 0; i < Vertices.Length; i++)
        {
            Vertices[i].Texture.Z = depthOffset;
        }
    }
    
    /// <summary>
    /// Updates the isometric billboard position for the supplied tile coordinates.
    /// </summary>
    /// <param name="newX">The destination tile X coordinate.</param>
    /// <param name="newY">The destination tile Y coordinate.</param>
    /// <param name="newZ">The destination tile Z coordinate.</param>
    public void UpdatePos(ushort newX, ushort newY, sbyte newZ)
    {
        var posX = newX * TILE_SIZE;
        var posY = newY * TILE_SIZE;
        var posZ = CEDGame.MapManager.FlatView ? 0 : newZ * TILE_Z_SCALE;

        float projectedWidth = TextureBounds.Width  * RSQRT2;
        float halfWidth = TextureBounds.Width * 0.5f;
        
        //Left half
        Vertices[0].Position = new Vector3(posX - projectedWidth, posY, posZ + TextureBounds.Height - halfWidth);
        Vertices[1].Position = new Vector3(posX, posY, posZ + TextureBounds.Height);
        Vertices[2].Position = new Vector3(posX - projectedWidth, posY, posZ - halfWidth);
        Vertices[3].Position = new Vector3(posX, posY, posZ);
        
        //Right Half
        Vertices[4].Position = new Vector3(posX, posY , posZ + TextureBounds.Height );
        Vertices[5].Position = new Vector3(posX, posY - projectedWidth, posZ + TextureBounds.Height - halfWidth);
        Vertices[6].Position = new Vector3(posX, posY , posZ);
        Vertices[7].Position = new Vector3(posX, posY - projectedWidth, posZ - halfWidth);
    }

    /// <summary>
    /// Applies a hue vector to the rendered static.
    /// </summary>
    /// <param name="newHue">The hue identifier to encode.</param>
    public void UpdateHue(ushort newHue)
    {
        var hueVec = HuesManager.Instance.GetHueVector(Tile.Id, newHue);
        for (int i = 0; i < Vertices.Length; i++)
        {
            Vertices[i].Hue = hueVec;
        }
    }

    /// <summary>
    /// Restores the static after preview or highlight state has been applied.
    /// </summary>
    public override void Reset()
    {
        base.Reset();
        UpdateHue(StaticTile.Hue);
        _ghostHue = -1;
        Highlighted = false;
    }
    
    private int _ghostHue = -1;
    
    /// <summary>
    /// Gets or sets the preview hue used by ghost rendering.
    /// </summary>
    public int GhostHue
    {
        get => _ghostHue;
        set
        {
            _ghostHue = value;
            var newHue = HuesManager.Instance.GetHueVector(Tile.Id, (ushort)_ghostHue);
            for (var index = 0; index < Vertices.Length; index++)
            {
                Vertices[index].Hue = newHue with { Z = Vertices[index].Hue.Z };
            }
        }
    }
    
    private const float LOW_ALPHA_VALUE = 0.5f;
    private const float HIGH_ALPHA_VALUE = 2.0f;
    
    private float HighlightAlpha => Config.Instance.ObjectBrightHighlight ? HIGH_ALPHA_VALUE : LOW_ALPHA_VALUE;

    /// <summary>
    /// Gets or sets a value indicating whether the static is currently highlighted in the editor.
    /// </summary>
    public bool Highlighted
    {
        get;
        set
        {
            field = value;
            var newAlpha = field ? HighlightAlpha : HuesManager.Instance.GetDefaultAlpha(Tile.Id);
            for (var index = 0; index < Vertices.Length; index++)
            {
                Vertices[index].Hue.Z = newAlpha;
            }
        }
    }

    /// <summary>
    /// Compares statics by draw priority altitude.
    /// </summary>
    /// <param name="other">The other static object.</param>
    /// <returns>A comparison result suitable for sorting.</returns>
    public int CompareTo(StaticObject? other)
    {
        if (ReferenceEquals(this, other))
        {
            return 0;
        }
        if (other is null)
        {
            return 1;
        }
        return StaticTile.PriorityZ.CompareTo(other.StaticTile.PriorityZ);
    }
}